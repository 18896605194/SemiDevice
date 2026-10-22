using System.Globalization;
using xyz.Common.Log;
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
/// 对准器模块基类：一个槽，片放上来自己对准，对好了才让机械手取走
/// </summary>
public abstract class BaseAlignerModule : BaseTransferStationModule, IAligner
{
    #region SV

    [VariableMark(VariableType.SV, ValueFormat.Int, description: "模块状态码")]
    public override int State { get; protected set; } = ModuleState.NotInit;

    #endregion

    #region SC

    [SCEditor("True", "Aligner", "是否启用对准器（False = 装机未接 / 停用：不登记槽位、不上线，调度不往这儿放片）")]
    public bool IsEnable { get; set; } = true;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Double, "deg", "0", "360", "0", "对准后缺口转到的角度（按设备自己的零位算）")]
    public double NotchAngle
    {
        get { return GetEcDouble(nameof(NotchAngle)); }
        set { SetEcDouble(nameof(NotchAngle), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "30000", description: "Home 动作超时（回原点；台上有片接着对准的那一段另按对准超时算）")]
    public int HomeTimeout
    {
        get { return GetEcInt(nameof(HomeTimeout)); }
        set { SetEcInt(nameof(HomeTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "30000", description: "对准动作超时")]
    public int AlignTimeout
    {
        get { return GetEcInt(nameof(AlignTimeout)); }
        set { SetEcInt(nameof(AlignTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "10000", description: "Reset 动作超时（清错）")]
    public int ResetTimeout
    {
        get { return GetEcInt(nameof(ResetTimeout)); }
        set { SetEcInt(nameof(ResetTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "10000", description: "Abort 动作超时（停止）")]
    public int AbortTimeout
    {
        get { return GetEcInt(nameof(AbortTimeout)); }
        set { SetEcInt(nameof(AbortTimeout), value); }
    }

    #endregion

    #region Alarm

    [Alarm("对准器动作失败", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "回原点、对准动作失败或超时（找不到缺口、真空没吸上、设备报错），或片放上来了却起不了对准，对准器进入错误状态",
        Solution = "确认台上实际有没有片、片是不是完好，账不对先调账；复位后重新回原点（台上有片会再对一遍）")]
    public string ControlledStopAlarm = nameof(ControlledStopAlarm);

    #endregion

    /// <summary>
    /// 对准器驱动；sc 里没挂开机就抛
    /// </summary>
    private IAlignerDriverComponent? _driver;

    private ILedgerManager? _ledger;

    /// <summary>
    /// 正在做（或最近一次做）的动作，动作收尾时按它判
    /// </summary>
    private AlignerAction _action;

    /// <summary>
    /// 这次回零 / 对准要对的那一片（发起时台上的片）；台上没片为 null
    /// </summary>
    private Guid? _aligningWafer;

    /// <summary>
    /// 对好的那一片；回零、对准一发起就作废，做成了才记上（只在内存，重启后台上的片要回零重新对）
    /// </summary>
    private Guid? _alignedWafer;

    /// <summary>
    /// 只有一个槽
    /// </summary>
    public override int SlotCount { get; set; } = 1;

    /// <summary>
    /// 站点类型：对准器
    /// </summary>
    public override StationKind Kind => StationKind.Aligner;

    protected BaseAlignerModule()
    {
        RegisterTransitions(AlignerStateTable.ToModuleTable());
    }

    #region 组件初始化

    /// <summary>
    /// 组件初始化（开机，不动硬件）：认驱动、登记晶圆账槽位；没有手动页，跟 buffer 一样开机就上线，全部初始化回零以后调度才会来
    /// </summary>
    public override bool InitComponent()
    {
        if (!IsEnable)
        {
            return true;
        }

        var driver = FindChild<IAlignerDriverComponent>();
        if (driver is null)
        {
            throw new InvalidOperationException($"对准器 {Name} 的 sc.xml 节点下没配驱动子组件（实现 IAlignerDriverComponent 的品牌驱动组件）。");
        }

        _driver = driver;
        _ledger = IocHelper.GetService<ILedgerManager>();
        _ledger?.RegisterLocation(Name, SlotCount);
        Online();
        return base.InitComponent();
    }

    /// <summary>
    /// 模块初始化 = Home；被拒记一条带原因的日志，返回 null
    /// </summary>
    public override ModuleOperation? InitModule()
    {
        var started = Home();
        if (!started.IsSuccess)
        {
            LogHelper.Warn(Name, $"初始化（Home）被拒：{started.ErrorMessage} [{string.Join(", ", started.Args)}]");
        }

        return started.Result;
    }

    #endregion

    #region 台上的片

    /// <summary>
    /// 台上那片对好了：账上有片，而且就是最近一次对准做成的那一片
    /// </summary>
    public bool IsWaferAligned
    {
        get
        {
            var wafer = WaferOnStage();
            lock (OperationGate)
            {
                return wafer is not null && _alignedWafer == wafer.Id;
            }
        }
    }

    /// <summary>
    /// 账上台上的那片；没片（或没装晶圆账）为 null
    /// </summary>
    private WaferInfo? WaferOnStage()
    {
        var ledger = _ledger;
        if (ledger is null)
        {
            return null;
        }

        return ledger.Get(Name, 1);
    }

    #endregion

    #region 跟机械手交互

    /// <summary>
    /// 待命之外再加一条：台上有片却没对好（对准失败、重启过、人改过账）不让机械手来，先回零重新对一遍
    /// </summary>
    public override HandleResult? CheckPrepareTransfer()
    {
        var rejected = base.CheckPrepareTransfer();
        if (rejected is not null)
        {
            return rejected;
        }

        if (WaferOnStage() is not null && !IsWaferAligned)
        {
            return HandleResult.Fail(ErrorCodes.AlignerWaferNotAligned, Name);
        }

        return null;
    }

    /// <summary>
    /// 一轮交互收尾：这一轮放了片就接着对准（机械手已经缩回，这一趟搬运照常收尾），对完回待命；只取走片的照常回待命。
    /// 起不了对准（驱动断了、互锁不让）落出错、报警，片留在台上没对过，调度不会来取
    /// </summary>
    protected override void OnTransferFinished(TransferContext context)
    {
        if (!context.HasPlace || WaferOnStage() is null)
        {
            base.OnTransferFinished(context);
            return;
        }

        var rejected = CheckState(AlignerAction.Align) ?? AlignInterlock();
        if (rejected is null && BeginAction(AlignerAction.Align, CreateAlignOperation()) is not null)
        {
            return;
        }

        string reason = ErrorCodes.ActionRejected;
        if (rejected is not null)
        {
            reason = $"{rejected.ErrorMessage} [{string.Join(", ", rejected.Args)}]";
        }

        LogHelper.Warn(Name, $"片放上来了，但起不了对准：{reason}");
        RaiseAlarm(ControlledStopAlarm);
        TransferStep(TransferModuleState.TransferComplete, ModuleState.Error);
    }

    #endregion

    #region 动作入口：状态 → 资源 → 互锁

    // 跟 LoadPort、腔体同一套：入口在模块锁里按 状态 → 资源 → 互锁 查，哪项不过就带原因返回、对准器不动，都过了才挂上设备动作。
    // 设备动作平台给默认（发驱动指令），机型做法不一样才重写 CreateXxxOperation，要加条件重写 XxxInterlock（先调 base）。

    /// <summary>
    /// 回零：台上有片的接着对一遍（回零转过台面，片的方向就不对了，对过才能让机械手取）。调用方：模块初始化（整机初始化）
    /// </summary>
    public HandleResult<ModuleOperation> Home()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(AlignerAction.Home) ?? HomeInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var started = BeginAction(AlignerAction.Home, CreateHomeOperation(WaferOnStage() is not null));
            if (started is null)
            {
                return HandleResult<ModuleOperation>.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
            }

            return HandleResult<ModuleOperation>.Success(started);
        }
    }

    /// <summary>
    /// 复位：先清报警、复位子组件，再挂上清设备报错的动作；只查状态，被拒的原因用 <see cref="CheckReset"/> 问
    /// </summary>
    public override ModuleOperation? Reset()
    {
        base.Reset();
        lock (OperationGate)
        {
            if (CheckReset() is not null)
            {
                return null;
            }

            return BeginAction(AlignerAction.Reset, CreateResetOperation());
        }
    }

    /// <summary>
    /// 复位能不能做（只查状态）
    /// </summary>
    public HandleResult? CheckReset()
    {
        return CheckState(AlignerAction.Reset);
    }

    /// <summary>
    /// 中止（急停）：先中止子组件，再挂上设备停止；能顶替在途动作，不清报警。只查停用和状态表，被拒的原因用 <see cref="CheckAbort"/> 问
    /// </summary>
    public override ModuleOperation? Abort()
    {
        base.Abort();
        lock (OperationGate)
        {
            if (CheckAbort() is not null)
            {
                return null;
            }

            return BeginAction(AlignerAction.Abort, CreateAbortOperation());
        }
    }

    /// <summary>
    /// 中止能不能做（只查停用和状态表）
    /// </summary>
    public HandleResult? CheckAbort()
    {
        return CheckState(AlignerAction.Abort);
    }

    /// <summary>
    /// 挂上动作，记下是哪个动作；回零、对准会转台面，一发起旧的"对好了"就作废，记下这次要对的片（在模块锁里调）
    /// </summary>
    private ModuleOperation? BeginAction(AlignerAction action, ModuleOperation operation)
    {
        var started = Begin(action, operation);
        if (started is null)
        {
            return null;
        }

        _action = action;
        if (action == AlignerAction.Home || action == AlignerAction.Align)
        {
            _alignedWafer = null;
            _aligningWafer = WaferOnStage()?.Id;
        }

        return started;
    }

    #endregion

    #region 设备动作（机型做法不一样就重写）

    /// <summary>
    /// 回零的设备动作：发 Home；台上有片再串一条对准
    /// </summary>
    protected virtual ModuleOperation CreateHomeOperation(bool hasWafer)
    {
        var operation = new AlignerCommandOperation("Home", () => _driver?.Home(), () => HomeTimeout);
        if (hasWafer)
        {
            operation.Then("Home 后对准", () => _driver?.Align(NotchAngle), () => AlignTimeout);
        }

        return operation;
    }

    /// <summary>
    /// 对准的设备动作：把缺口转到 EC NotchAngle
    /// </summary>
    protected virtual ModuleOperation CreateAlignOperation()
    {
        return new AlignerCommandOperation("Align", () => _driver?.Align(NotchAngle), () => AlignTimeout);
    }

    /// <summary>
    /// 复位的设备动作：清设备报错
    /// </summary>
    protected virtual ModuleOperation CreateResetOperation()
    {
        return new AlignerCommandOperation("Reset", () => _driver?.ResetDrive(), () => ResetTimeout);
    }

    /// <summary>
    /// 中止的设备动作：停止
    /// </summary>
    protected virtual ModuleOperation CreateAbortOperation()
    {
        return new AlignerCommandOperation("Abort", () => _driver?.Stop(), () => AbortTimeout);
    }

    #endregion

    #region Interlock

    // 互锁：周围条件允不允许动。平台给默认，机型有别的条件重写、先调 base，用机型自己的错误码。

    /// <summary>
    /// 回零前：平台不拦
    /// </summary>
    protected virtual HandleResult? HomeInterlock()
    {
        return null;
    }

    /// <summary>
    /// 对准前：平台不拦
    /// </summary>
    protected virtual HandleResult? AlignInterlock()
    {
        return null;
    }

    #endregion

    /// <summary>
    /// 装机停用、没挂驱动的不发动作
    /// </summary>
    protected override bool CanBeginAction => IsEnable && _driver is not null;

    /// <summary>
    /// 能不能动作：停用、没挂驱动之外，驱动没连上也不发
    /// </summary>
    protected override HandleResult? CheckAvailable()
    {
        var unavailable = base.CheckAvailable();
        if (unavailable is not null)
        {
            return unavailable;
        }

        var driver = _driver;
        if (driver is null || !driver.IsConnected)
        {
            return HandleResult.Fail(ErrorCodes.ModuleNotConnected, Name);
        }

        return null;
    }

    /// <summary>
    /// 操作终结（状态已由基类落好）：回零、对准做成了才把发起时台上那片记成对好了；没做成的报警、作废在途指令
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        RaiseActionFailedAlarm(operation);
        if (!operation.IsSuccess)
        {
            _driver?.AbandonAll("Abandoned");
            return;
        }

        if (_action == AlignerAction.Home || _action == AlignerAction.Align)
        {
            _alignedWafer = _aligningWafer;
        }
    }

    /// <summary>
    /// 动作类报警：失败就报，人为急停顶掉的不报；只能人工复位清。机型有自己的报警分法就重写（在模块锁里，只报警、别等待）
    /// </summary>
    protected virtual void RaiseActionFailedAlarm(ModuleOperation operation)
    {
        if (!operation.IsSuccess && operation.Code != ErrorCodes.Aborted)
        {
            RaiseAlarm(ControlledStopAlarm);
        }
    }
}
