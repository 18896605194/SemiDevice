using System.Globalization;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// 整机（sc.xml 的 System 节点）：系统状态和整机动作——全部初始化、Auto / Manual、整机停止、全部回片；
/// 顺带放全系统一份的设置（界面语言）。状态码见 <see cref="SystemState"/>，状态表见 <see cref="SystemStateTable"/>。
/// 系统状态只说整机在干什么，不汇总各模块：单个模块报警不改系统状态（看报警条数和四色灯）。
/// Auto 落地靠搬运管理的自动派单（Job 只看那个开关），开关只由这里拨。
/// </summary>
[Component(description: "整机：系统状态、全部初始化、Auto / Manual、整机停止、全部回片；界面语言")]
public class SystemModule : BaseModule, ISystemModule
{
    public SystemModule()
    {
        RegisterTransitions(SystemStateTable.ToModuleTable());
    }

    #region SC

    /// <summary>
    /// 界面语言：客户端启动时按它加载语言包（Strings.{Language}.xaml）。
    /// </summary>
    [SCEditor("zh-CN", "System", "界面语言：zh-CN 简体中文，en-US 英文；客户端启动时按它显示，改了重启客户端生效")]
    public string Language { get; set; } = "zh-CN";

    #endregion

    #region SV

    [VariableMark(VariableType.SV, ValueFormat.Int, description: "系统状态码")]
    public override int State { get; protected set; } = ModuleState.NotInit;

    #endregion

    /// <summary>Auto：自动空闲或自动运行。</summary>
    public bool IsAuto
    {
        get
        {
            int state = State;
            return state == SystemState.AutoIdle || state == SystemState.AutoRunning;
        }
    }

    #region 装配

    private IReadOnlyList<IModule> _modules = [];

    // 下面几个单例开机绑模块表时取；没装是 null
    private ITransferManager? _transfers;
    private IJobManager? _jobs;
    private IWaferManagerComponent? _ledger;
    private IAlarmComponent? _alarms;
    private IWaferSizeComponent? _waferSize;

    /// <summary>
    /// 开机：认整机要管的模块（全部初始化、整机停止、全部回片）
    /// </summary>
    public override void Bind()
    {
        Bind(IocHelper.GetServices<IModule>());
    }

    /// <summary>
    /// 绑模块表（开机，模块开始扫描之前）：全部初始化、整机停止管的就是这些模块（不含自己），先后跟 sc.xml 一样；
    /// 搬运管理、Job 管理、晶圆账、报警、晶圆尺寸也在这里取一次。
    /// </summary>
    public void Bind(IEnumerable<IModule> modules)
    {
        _transfers = IocHelper.GetService<ITransferManager>();
        _jobs = IocHelper.GetService<IJobManager>();
        _ledger = IocHelper.GetService<IWaferManagerComponent>();
        _alarms = IocHelper.GetService<IAlarmComponent>();
        _waferSize = IocHelper.GetService<IWaferSizeComponent>();
        _modules = modules.Where(module => !ReferenceEquals(module, this)).ToList();
        LogHelper.Info(Name, $"整机管 {_modules.Count} 个模块：{string.Join(", ", _modules.Select(module => module.Name))}");
    }

    #endregion

    #region 动作入口

    /// <summary>
    /// 全部初始化：先机械手回原点，再一起初始化其余模块（见 <see cref="SystemInitOperation"/>），都做成了进空闲。
    /// 有一个被拒或没做成整机回未初始化，原因在日志里（模块自己也记、也报警）。
    /// </summary>
    public HandleResult<ModuleOperation> InitAll()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(SystemAction.Init);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(SystemAction.Init, new SystemInitOperation(_modules));
        }
    }

    /// <summary>
    /// 切 Auto：要先全部初始化过（空闲）；开搬运管理的自动派单，Job 才派活。当场进自动空闲，有在跑的 Job 下一拍转自动运行。
    /// 没装搬运管理回 transfer.not_installed，搬运管理停用了回 transfer.disabled。
    /// </summary>
    public HandleResult Auto()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(SystemAction.Auto);
            if (rejected is not null)
            {
                return rejected;
            }

            var transfers = _transfers;
            if (transfers is null)
            {
                return HandleResult.Fail(ErrorCodes.TransferNotInstalled);
            }

            if (!transfers.IsEnable)
            {
                return HandleResult.Fail(ErrorCodes.TransferDisabled);
            }

            transfers.StartAutoDispatch();
            SwitchTo(SystemAction.Auto);
            return HandleResult.Success();
        }
    }

    /// <summary>
    /// 切 Manual：关自动派单，Job 不再派新动作，已经在做的那一趟做完；回空闲。
    /// </summary>
    public HandleResult Manual()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(SystemAction.Manual);
            if (rejected is not null)
            {
                return rejected;
            }

            _transfers?.StopAutoDispatch();
            SwitchTo(SystemAction.Manual);
            return HandleResult.Success();
        }
    }

    /// <summary>
    /// 全部回片：Manual 空闲时，机内的片回各自来源 LoadPort 的原槽（见 <see cref="SystemReturnOperation"/>），回完回空闲。
    /// 要搬运管理和晶圆账都开着。
    /// </summary>
    public HandleResult<ModuleOperation> ReturnAll()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(SystemAction.ReturnAll) ?? CheckReturnResource();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            // 上面查过搬运管理、晶圆账都装着
            return Started(SystemAction.ReturnAll, new SystemReturnOperation(Name, _transfers!, _ledger!, _jobs));
        }
    }

    /// <summary>回片要用的：搬运管理装了、开着，晶圆账装了、开着。</summary>
    private HandleResult? CheckReturnResource()
    {
        var transfers = _transfers;
        if (transfers is null)
        {
            return HandleResult.Fail(ErrorCodes.TransferNotInstalled);
        }

        if (!transfers.IsEnable)
        {
            return HandleResult.Fail(ErrorCodes.TransferDisabled);
        }

        var ledger = _ledger;
        if (ledger is null || !ledger.IsEnable)
        {
            return HandleResult.Fail(ErrorCodes.WaferLedgerDisabled);
        }

        return null;
    }

    /// <summary>
    /// 整机停止（界面 Stop）：签名跟着组件基类；停止中再按不收，被拒的原因用 <see cref="CheckAbort"/> 问。
    /// 先进停止中（顶掉在做的全部初始化、全部回片），再在锁外：关自动派单、撤掉所有搬运（机械手正在取放的由搬运管理发设备中止）；
    /// 正在做别的动作的模块直接发中止——正在搬运的机械手、给 Job 做工艺的腔体不在这里发，归搬运管理和 Job 的中止去停；
    /// 最后 Job 全部走中止（等设备确认、核对片位，不是直接删 Job）。等停的那一刻各模块手上的动作都收完尾，回到停之前该在的地方（见状态表）。
    /// </summary>
    public override ModuleOperation? Abort()
    {
        base.Abort();

        SystemStopOperation stop;
        lock (OperationGate)
        {
            if (CheckAbort() is not null)
            {
                return null;
            }

            var inFlight = new List<ModuleOperation>();
            foreach (var module in _modules)
            {
                var current = module.CurrentOperation;
                if (current is not null)
                {
                    inFlight.Add(current);
                }
            }

            stop = new SystemStopOperation(inFlight, _transfers);
            if (Begin(SystemAction.Abort, stop) is null)
            {
                return null;
            }
        }

        // 锁外发：Job 中止要等 Job 的锁，别让系统的扫描线程跟着等
        var transfers = _transfers;
        transfers?.Abort();

        // 要先认再让 Job 中止：Job 的中止当场就给它的腔体发中止，之后就认不出那是 Job 的工艺了
        int aborted = 0;
        foreach (var module in _modules)
        {
            if (module.CurrentOperation is null)
            {
                continue;
            }

            bool robotInTransfer = module is IRobot robot && transfers is not null && transfers.IsRobotInTransfer(robot.Name);
            if (robotInTransfer || JobManager.IsJobProcess(module))
            {
                continue;
            }

            module.Abort();
            aborted++;
        }

        var jobs = _jobs;
        if (jobs is not null)
        {
            _ = jobs.AbortAllAsync();
        }

        LogHelper.Info(Name, $"整机停止：自动派单已关，搬运已撤，Job 走中止；另有 {aborted} 个模块在做动作，已发中止");
        stop.Issue();
        return stop;
    }

    public HandleResult? CheckAbort()
    {
        return CheckState(SystemAction.Abort);
    }

    /// <summary>
    /// 检查都过了，挂上动作；挂不上（检查和挂在同一把锁里，正常不会）回笼统的被拒 + 当前状态。
    /// </summary>
    private HandleResult<ModuleOperation> Started(SystemAction action, ModuleOperation operation)
    {
        var started = Begin(action, operation);
        if (started is null)
        {
            return HandleResult<ModuleOperation>.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
        }

        LogHelper.Info(Name, $"开始 {action}");
        return HandleResult<ModuleOperation>.Success(started);
    }

    /// <summary>当场切的动作（Auto / Manual）：不挂动作，直接落状态表的成功态。调用方已在锁里查过状态。</summary>
    private void SwitchTo(SystemAction action)
    {
        if (TryGetTransition(State, action.ToString(), out var transition))
        {
            State = transition.SuccessState;
            LogHelper.Info(Name, $"{action}");
        }
    }

    #endregion

    #region 扫描、收尾、推送

    /// <summary>
    /// 扫描周期：步进在做的动作；Auto 下按有没有在跑的 Job 切自动运行 / 自动空闲；推状态。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        FollowJobs();
        PublishState();
        PublishEquipmentStatus();
    }

    private void FollowJobs()
    {
        if (!IsAuto)
        {
            return;
        }

        // 先在锁外问 Job（要等 Job 的锁），再进自己的锁核对一遍还在 Auto
        var jobs = _jobs;
        int next = jobs is not null && jobs.HasRunningJob ? SystemState.AutoRunning : SystemState.AutoIdle;
        lock (OperationGate)
        {
            int state = State;
            if (state != SystemState.AutoIdle && state != SystemState.AutoRunning)
            {
                return;
            }

            if (next != state)
            {
                State = next;
                LogHelper.Info(Name, next == SystemState.AutoRunning ? "有 Job 在跑：自动运行" : "Job 都结束了：自动空闲");
            }
        }
    }

    /// <summary>
    /// 动作没做成：基类已落 Error，报警逻辑还没接，先不进 Error——全部初始化没做成回未初始化，全部回片没做成回空闲。
    /// 原因记日志（初始化是哪个模块被拒 / 没做成，回片是几片没回去，每片的原因前面已经记了）。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        if (operation.IsSuccess)
        {
            LogHelper.Info(Name, $"{operation.Name} 做完");
            return;
        }

        if (operation is SystemInitOperation)
        {
            State = ModuleState.NotInit;
            LogHelper.Warn(Name, $"全部初始化没做成：{operation.Reason}");
        }
        else if (operation is SystemReturnOperation)
        {
            State = ModuleState.Idle;
            LogHelper.Warn(Name, $"全部回片没做完：{operation.Reason}");
        }
    }

    private SystemStateDto? _lastPublished;

    /// <summary>系统状态有变化才推（留存，晚连上的界面订上就拿到当前值）。</summary>
    protected override void PublishState()
    {
        var dto = new SystemStateDto { State = State };
        var last = _lastPublished;
        if (last is not null && last.State == dto.State)
        {
            return;
        }

        _lastPublished = dto;
        EventBus.Send(dto, SystemStateDto.EventToken);
    }

    #endregion

    #region 设备总状态（顶栏四色灯）

    private (bool HasAlarm, bool HasWarning, bool IsRunning, int WaferSize)? _lastStatus;

    private string? _lastStatusFault;

    /// <summary>
    /// 四色灯输出失败时记过的原因：同一个灯同样的错只记一次
    /// </summary>
    private readonly Dictionary<ILightComponent, string> _lightErrors = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// 每拍算一次设备总状态，变了才点亮下面挂的四色灯、推给顶栏
    /// </summary>
    private void PublishEquipmentStatus()
    {
        try
        {
            var dto = CreateEquipmentStatus();
            var status = (dto.HasAlarm, dto.HasWarning, dto.IsRunning, dto.WaferSize);
            if (_lastStatus == status)
            {
                return;
            }

            // 只是晶圆尺寸变了也重刷一遍灯：写的是同样的值，尺寸又是人点的、很少变
            _lastStatus = status;
            foreach (var light in FindChildren<ILightComponent>())
            {
                Drive(light, dto.HasAlarm, dto.HasWarning, dto.IsRunning);
            }

            EventBus.Send(dto, EquipmentStatusDto.EventToken);
            _lastStatusFault = null;
        }
        catch (Exception exception)
        {
            if (_lastStatusFault != exception.Message)
            {
                _lastStatusFault = exception.Message;
                LogHelper.Warn(Name, $"设备总状态刷新失败: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// 这一刻的设备总状态：报警看报警组件（Warn 级算警告，其余算报警），运行看有没有模块（含整机自己）在做动作，晶圆尺寸看晶圆尺寸组件
    /// </summary>
    public EquipmentStatusDto CreateEquipmentStatus()
    {
        IReadOnlyList<AlarmItem> alarms = [];
        var alarmComponent = _alarms;
        if (alarmComponent is not null)
        {
            alarms = alarmComponent.ActiveAlarms;
        }

        return new EquipmentStatusDto
        {
            HasAlarm = alarms.Any(alarm => alarm.Level != AlarmLevel.Warn),
            HasWarning = alarms.Any(alarm => alarm.Level == AlarmLevel.Warn),
            IsRunning = CurrentOperation is not null || _modules.Any(module => module.CurrentOperation is not null),
            WaferSize = WaferSizeComponent.SizeOf(_waferSize),
        };
    }

    private void Drive(ILightComponent light, bool red, bool yellow, bool green)
    {
        try
        {
            light.SetRed(red);
            light.SetYellow(yellow);
            light.SetGreen(green);
            _lightErrors.Remove(light);
        }
        catch (Exception exception)
        {
            if (_lightErrors.TryGetValue(light, out var last) && last == exception.Message)
            {
                return;
            }

            _lightErrors[light] = exception.Message;
            string name = light.GetType().Name;
            if (light is ComponentBase component)
            {
                name = component.FullPath;
            }

            LogHelper.Warn(Name, $"{name} 四色灯输出失败: {exception.Message}");
        }
    }

    #endregion
}
