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


[Component(description: "整机：系统状态、全部初始化、Auto / Manual、整机停止、全部回片；界面语言")]
public class SystemModule : BaseStateComponent, ISystemModule
{
    public SystemModule()
    {
        RegisterTransitions(SystemStateTable.ToModuleTable());
    }

    #region Machine

    /// <summary>
    /// 界面语言：客户端启动时按它加载语言包（Strings.{Language}.xaml）。
    /// </summary>
    [Machine("zh-CN", "System", "界面语言：zh-CN 简体中文，en-US 英文；客户端启动时按它显示，改了重启客户端生效")]
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

    /// <summary>
    /// 在做的全部初始化（收尾时认它）；没在做为 null
    /// </summary>
    private ModuleRoutine? _initRoutine;

    // 下面几个单例开机绑模块表时取；没装是 null
    private ITransferManager? _transfers;
    private IJobManager? _jobs;
    private ILedgerManager? _ledger;
    private IAlarm? _alarms;
    private IWaferSize? _waferSize;

    /// <summary>
    /// 开机：认整机要管的模块（全部初始化、整机停止、全部回片）
    /// </summary>
    public override void Bind()
    {
        Bind(IocHelper.GetServices<IModule>());
    }

    /// <summary>
    /// 绑模块表（开机，模块开始扫描之前）：全部初始化、整机停止管的就是这些模块，先后跟 machine.xml 一样；
    /// 搬运管理、Job 管理、晶圆账、报警、晶圆尺寸也在这里取一次。
    /// </summary>
    public void Bind(IEnumerable<IModule> modules)
    {
        _transfers = IocHelper.GetService<ITransferManager>();
        _jobs = IocHelper.GetService<IJobManager>();
        _ledger = IocHelper.GetService<ILedgerManager>();
        _alarms = IocHelper.GetService<IAlarm>();
        _waferSize = IocHelper.GetService<IWaferSize>();
        _modules = modules.ToList();
        LogHelper.Info(this, $"整机管 {_modules.Count} 个模块：{string.Join(", ", _modules.Select(module => module.Name))}");
    }

    #endregion

    #region 动作入口

    /// <summary>
    /// 全部初始化：先机械手回原点（手缩回来，别的站点动起来才不刮手），都做成了再一起初始化其余模块（见 <see cref="SystemInitStep"/>），都做成了进空闲。
    /// 有一个被拒或没做成整机回未初始化，原因在日志里（模块自己也记、也报警）。
    /// </summary>
    public HandleResult<ModuleRoutine> InitAll()
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(SystemAction.Init);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var init = new ModuleRoutine(this, "Init");
            var robots = _modules.Where(module => module is IRobot).ToList();
            if (robots.Count > 0)
            {
                init.Add(new SystemInitStep("机械手回原点", robots));
            }

            var others = _modules.Where(module => module is not IRobot).ToList();
            if (others.Count > 0)
            {
                init.Add(new SystemInitStep("其余模块初始化", others));
            }

            var started = Started(SystemAction.Init, init);
            if (started.IsSuccess)
            {
                _initRoutine = init;
            }

            return started;
        }
    }

    /// <summary>
    /// 切 Auto：要先全部初始化过（空闲）；开搬运管理的自动派单，Job 才派活。当场进自动空闲，有在跑的 Job 下一拍转自动运行。
    /// 没装搬运管理回 transfer.not_installed，搬运管理停用了回 transfer.disabled。
    /// </summary>
    public HandleResult Auto()
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(SystemAction.Auto);
            if (rejected is not null)
            {
                return rejected;
            }

            if (_transfers is null)
            {
                return HandleResult.Fail(ErrorCodes.TransferNotInstalled);
            }

            if (!_transfers.IsEnable)
            {
                return HandleResult.Fail(ErrorCodes.TransferDisabled);
            }

            _transfers.StartAutoDispatch();
            SwitchTo(SystemAction.Auto);
            return HandleResult.Success();
        }
    }

    /// <summary>
    /// 切 Manual：关自动派单，Job 不再派新动作，已经在做的那一趟做完；回空闲。
    /// </summary>
    public HandleResult Manual()
    {
        lock (RoutineGate)
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
    /// 全部回片：整机 Idle 时，机内的片倒着走回各自来源 LoadPort 的原槽（见 <see cref="SystemReturnRoutine"/>），回完回 Idle
    /// </summary>
    public HandleResult<ModuleRoutine> ReturnAll()
    {
        ITransferManager transfers;
        ILedgerManager ledger;
        lock (RoutineGate)
        {
            var rejected = CheckState(SystemAction.ReturnAll) ?? CheckReturnResource();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            // 上面查过搬运管理、晶圆账都装着
            transfers = _transfers!;
            ledger = _ledger!;
        }

        // 开始前的检查在锁外做：要问 Job、各设备，不拿着整机的锁去等别人的锁
        var prepared = SystemReturnRoutine.Prepare(this, transfers, ledger, _jobs);
        var routine = prepared.Result;
        if (!prepared.IsSuccess || routine is null)
        {
            LogHelper.Warn(this, $"全部回片不能开始：{prepared.ErrorMessage} [{string.Join(", ", prepared.Args)}]");
            return prepared;
        }

        lock (RoutineGate)
        {
            var rejected = CheckState(SystemAction.ReturnAll);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(SystemAction.ReturnAll, routine);
        }
    }

    /// <summary>回片要用的：搬运管理装了、开着，晶圆账装了、开着。</summary>
    private HandleResult? CheckReturnResource()
    {
        if (_transfers is null)
        {
            return HandleResult.Fail(ErrorCodes.TransferNotInstalled);
        }

        if (!_transfers.IsEnable)
        {
            return HandleResult.Fail(ErrorCodes.TransferDisabled);
        }

        if (_ledger is null || !_ledger.IsEnable)
        {
            return HandleResult.Fail(ErrorCodes.WaferLedgerDisabled);
        }

        return null;
    }

    /// <summary>
    /// 整机停止（界面 Stop）：停止中再按不收，被拒带原因。
    /// 先进停止中（顶掉在做的全部初始化、全部回片），再在锁外：关自动派单、撤掉所有搬运（机械手正在取放的由搬运管理发设备中止）；
    /// 正在做别的动作的模块直接发中止——正在搬运的机械手、给 Job 做工艺的腔体不在这里发，归搬运管理和 Job 的中止去停；
    /// 最后 Job 全部走中止（等设备确认、核对片位，不是直接删 Job）。等停的那一刻各模块手上的动作都收完尾，回到停之前该在的地方（见状态表）。
    /// </summary>
    public override HandleResult<ModuleRoutine> Abort()
    {
        base.Abort();

        SystemStopStep wait;
        HandleResult<ModuleRoutine> started;
        lock (RoutineGate)
        {
            var rejected = CheckState(SystemAction.Abort);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var inFlight = new List<ModuleRoutine>();
            foreach (var module in _modules)
            {
                var current = module.CurrentRoutine;
                if (current is not null)
                {
                    inFlight.Add(current);
                }
            }

            wait = new SystemStopStep("等各模块停下", inFlight, _transfers);
            started = Started(SystemAction.Abort, new ModuleRoutine(this, "Abort").Add(wait));
            if (!started.IsSuccess)
            {
                return started;
            }
        }

        // 锁外发：Job 中止要等 Job 的锁，别让系统的扫描线程跟着等
        _transfers?.Abort();

        // 要先认再让 Job 中止：Job 的中止当场就给它的腔体发中止，之后就认不出那是 Job 的工艺了
        int aborted = 0;
        foreach (var module in _modules)
        {
            if (module.CurrentRoutine is null)
            {
                continue;
            }

            bool robotInTransfer = module is ITransferRobot robot && _transfers is not null && _transfers.IsRobotInTransfer(robot.Name);
            if (robotInTransfer || JobManager.IsJobProcess(module))
            {
                continue;
            }

            module.Abort();
            aborted++;
        }

        if (_jobs is not null)
        {
            _ = _jobs.AbortAllAsync();
        }

        LogHelper.Info(this, $"整机停止：自动派单已关，搬运已撤，Job 走中止；另有 {aborted} 个模块在做动作，已发中止");
        wait.Issue();
        return started;
    }

    /// <summary>
    /// 检查都过了，挂上动作；挂不上（检查和挂在同一把锁里，正常不会）回笼统的被拒 + 当前状态。
    /// </summary>
    private HandleResult<ModuleRoutine> Started(SystemAction action, ModuleRoutine routine)
    {
        var started = Begin(action, routine);
        if (started is null)
        {
            return HandleResult<ModuleRoutine>.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
        }

        LogHelper.Info(this, $"开始 {action}");
        return HandleResult<ModuleRoutine>.Success(started);
    }

    /// <summary>当场切的动作（Auto / Manual）：不挂动作，直接落状态表的成功态。调用方已在锁里查过状态。</summary>
    private void SwitchTo(SystemAction action)
    {
        if (TryGetTransition(State, action.ToString(), out var transition))
        {
            State = transition.SuccessState;
            LogHelper.Info(this, $"{action}");
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
        PublishModuleStatus();
    }

    private void FollowJobs()
    {
        if (!IsAuto)
        {
            return;
        }

        // 先在锁外问 Job（要等 Job 的锁），再进自己的锁核对一遍还在 Auto
        int next = _jobs is not null && _jobs.HasRunningJob ? SystemState.AutoRunning : SystemState.AutoIdle;
        lock (RoutineGate)
        {
            int state = State;
            if (state != SystemState.AutoIdle && state != SystemState.AutoRunning)
            {
                return;
            }

            if (next != state)
            {
                State = next;
                LogHelper.Info(this, next == SystemState.AutoRunning ? "有 Job 在跑：自动运行" : "Job 都结束了：自动空闲");
            }
        }
    }

    /// <summary>
    /// 动作没做成：基类已落 Error，报警逻辑还没接，先不进 Error——全部初始化没做成回未初始化，全部回片没做成回空闲。
    /// 原因记日志（初始化是哪个模块被拒 / 没做成，回片是几片没回去，每片的原因前面已经记了）；做成的动作自己记了每一步的用时
    /// </summary>
    protected override void OnRoutineCompleted(ModuleRoutine routine)
    {
        var init = _initRoutine;
        _initRoutine = null;
        if (routine.IsSuccess)
        {
            return;
        }

        if (ReferenceEquals(routine, init))
        {
            State = ModuleState.NotInit;
            LogHelper.Warn(this, $"全部初始化没做成：{routine.Reason}");
        }
        else if (routine is SystemReturnRoutine)
        {
            State = ModuleState.Idle;
            LogHelper.Warn(this, $"全部回片没做完：{routine.Reason}");
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

    private ModuleStatusListDto? _lastModuleStatus;

    /// <summary>
    /// 推各模块的状态和生产 / 维护（顶栏的模块状态行用）：有一个变了才推整张表，留存
    /// </summary>
    private void PublishModuleStatus()
    {
        if (!HasModuleStatusChanged())
        {
            return;
        }

        var list = new ModuleStatusListDto();
        foreach (var module in _modules)
        {
            list.Modules.Add(new ModuleStatusDto
            {
                Name = module.Name,
                Kind = module.Kind,
                State = module.State,
                Mode = ModeOf(module),
            });
        }

        _lastModuleStatus = list;
        EventBus.Send(list, ModuleStatusListDto.EventToken);
    }

    /// <summary>
    /// 跟上次推的比：模块表换了（重新 Bind）、哪个模块的状态或生产 / 维护变了
    /// </summary>
    private bool HasModuleStatusChanged()
    {
        var last = _lastModuleStatus;
        if (last is null || last.Modules.Count != _modules.Count)
        {
            return true;
        }

        for (int i = 0; i < _modules.Count; i++)
        {
            var module = _modules[i];
            var sent = last.Modules[i];
            if (sent.Name != module.Name || sent.State != module.State || sent.Mode != ModeOf(module))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 模块的生产 / 维护；没有这个模式的（供液）是 null
    /// </summary>
    private static ModuleMode? ModeOf(IModule module)
    {
        if (module is IMode target)
        {
            return target.Mode;
        }

        return null;
    }

    #endregion

    #region 设备总状态（顶栏四色灯）

    private (bool HasAlarm, bool HasWarning, bool IsRunning, int WaferSize)? _lastStatus;

    private string? _lastStatusFault;

    /// <summary>
    /// 四色灯输出失败时记过的原因：同一个灯同样的错只记一次
    /// </summary>
    private readonly Dictionary<ILight, string> _lightErrors = new(ReferenceEqualityComparer.Instance);

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
            foreach (var light in FindChildren<ILight>())
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
                LogHelper.Warn(this, $"设备总状态刷新失败: {exception.Message}");
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
            IsRunning = CurrentRoutine is not null || _modules.Any(module => module.CurrentRoutine is not null),
            WaferSize = WaferSizeComponent.SizeOf(_waferSize),
        };
    }

    private void Drive(ILight light, bool red, bool yellow, bool green)
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

            LogHelper.Warn(this, $"{name} 四色灯输出失败: {exception.Message}");
        }
    }

    #endregion
}
