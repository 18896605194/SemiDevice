using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// 机械手可服务工位基类
/// </summary>
public abstract class BaseTransferStationModule : BaseModule, ITransferStation, IStationTaskExecutor, IMode
{
    #region 生产 / 维护模式

    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "模块模式（生产 / 维护）")]
    public ModuleMode Mode { get; private set; } = ModuleMode.Maintenance;

    /// <summary>
    /// 切生产模式：交给调度用，只改 Mode、不经设备；机型要多做事就重写、记得调 base
    /// </summary>
    public virtual void SetProduction()
    {
        SetMode(ModuleMode.Production);
    }

    /// <summary>
    /// 切维护模式：调度不再派新活，手上的做完；重写同 <see cref="SetProduction"/>
    /// </summary>
    public virtual void SetMaintenance()
    {
        SetMode(ModuleMode.Maintenance);
    }

    private void SetMode(ModuleMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        LogHelper.Info($"[{Name}] {mode}");
    }

    #endregion

    #region SC

    [SCEditor("False", "TransferStation",
        "取片要站点中途交接：机械手伸到位先停下，站点把片交到手上（比如降顶针）再缩臂；到这个站的机械手驱动要有分段指令。False = 机械手一条指令取完")]
    public bool HandoffOnPick { get; set; }

    [SCEditor("False", "TransferStation",
        "放片要站点中途交接：机械手伸到位先停下，站点把片接走（比如升顶针）再缩臂；到这个站的机械手驱动要有分段指令。False = 机械手一条指令放完")]
    public bool HandoffOnPlace { get; set; }

    #endregion

    #region 待命态（传片环的起点和终点）

    /// <summary>
    /// 待命态：站点空着、机械手可以来取放片的状态，传片环从这里出发、做完回到这里。
    /// 各站点不一样：腔体是 Idle，LoadPort 是 Loaded（载具装好了才能取放）。
    /// </summary>
    protected virtual int StandbyState => ModuleState.Idle;

    #endregion

    #region 在位传感器

    /// <summary>
    /// 在位传感器：sc 里站点下面叫 WaferSensor 的 DI；没配为 null
    /// </summary>
    protected IDiSensor? WaferSensor { get; private set; }

    /// <summary>
    /// 晶圆账（组件初始化时取），在位传感器对账用；没配为 null
    /// </summary>
    private ILedgerManager? _sensorLedger;

    /// <summary>
    /// 组件初始化：认在位传感器、取晶圆账，子组件交给基类
    /// </summary>
    public override bool InitComponent()
    {
        WaferSensor = FindChild<IDiSensor>("WaferSensor");
        _sensorLedger = IocHelper.GetService<ILedgerManager>();
        return base.InitComponent();
    }

    /// <summary>
    /// 在位传感器跟晶圆账对一遍：配了才比（看到有片 = 账上这个站有片），配了读不到也算对不上；传感器配法不一样的站点重写
    /// </summary>
    public virtual HandleResult? CheckWaferSensors()
    {
        var sensor = WaferSensor;
        var ledger = _sensorLedger;
        if (sensor is null || !sensor.IsConfigured || ledger is null || !ledger.IsEnable)
        {
            return null;
        }

        bool? sensed = sensor.IsActive;
        if (sensed is null)
        {
            return HandleResult.Fail(ErrorCodes.StationWaferSensorUnknown, Name);
        }

        bool booked = ledger.CountWafers(Name) > 0;
        if (sensed.Value == booked)
        {
            return null;
        }

        if (sensed.Value)
        {
            return HandleResult.Fail(ErrorCodes.StationWaferSensorHasWafer, Name);
        }

        return HandleResult.Fail(ErrorCodes.StationWaferSensorNoWafer, Name);
    }

    #endregion

    #region ITransferStation 实现

    private TransferContext? _transferContext;

    /// <summary>
    /// 槽位数
    /// </summary>
    public abstract int SlotCount { get; set; }

    public bool CanPrepareTransfer => CheckPrepareTransfer() is null;

    /// <summary>
    /// 只检查传片环是否在待命态；模块补充自己的准备条件，不在这里增加自动调度限制。
    /// </summary>
    public virtual HandleResult? CheckPrepareTransfer()
    {
        lock (OperationGate)
        {
            int state = State;
            if (state != StandbyState)
            {
                return HandleResult.Fail(ErrorCodes.ModuleStateNotAllowed, Name, state.ToString(CultureInfo.InvariantCulture));
            }

            if (HasUnsettledOperation())
            {
                return HandleResult.Fail(ErrorCodes.ModuleBusy, Name);
            }

            return null;
        }
    }

    /// <summary>
    /// 准备一：锁内重查准入并占用本轮传片环。
    /// </summary>
    public ModuleOperation? PrepareTransfer(TransferContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (OperationGate)
        {
            if (HasUnsettledOperation() || CheckPrepareTransfer() is not null)
            {
                return null;
            }

            // 恢复动作可能绕过 TransferStep；旧上下文不代表仍在占用，新一轮准入后直接覆盖。
            var previous = _transferContext;
            _transferContext = context;
            var operation = PrepareTransferCore(context);
            if (operation is null)
            {
                _transferContext = previous;
            }

            return operation;
        }
    }

    /// <summary>
    /// 准备二
    /// </summary>
    public ModuleOperation? PrepareTransfer2(TransferContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (OperationGate)
        {
            if (!ReferenceEquals(_transferContext, context)
                || State != TransferModuleState.PreTransfer
                || HasUnsettledOperation())
            {
                return null;
            }

            return PrepareTransfer2Core(context);
        }
    }

    /// <summary>
    /// 机械手和工站正在交互
    /// </summary>
    public bool Transferring(TransferContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (OperationGate)
        {
            if (!ReferenceEquals(_transferContext, context) || HasUnsettledOperation())
            {
                return false;
            }

            // 同站换槽取完不收尾，放片前仍须核对本轮身份及有无恢复动作。
            return State == TransferModuleState.Transferring
                || TransferStep(TransferModuleState.TransferReady, TransferModuleState.Transferring);
        }
    }

    /// <summary>
    /// 换段准备：一轮先取后放时取完、放之前调；只认同一份两段上下文、交互中、没有未收尾的动作，不受理返回 null
    /// </summary>
    public ModuleOperation? PrepareNextLeg(TransferContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (OperationGate)
        {
            if (!ReferenceEquals(_transferContext, context)
                || !context.HasPick
                || !context.HasPlace
                || State != TransferModuleState.Transferring
                || HasUnsettledOperation())
            {
                return null;
            }

            return PrepareNextLegCore(context);
        }
    }

    /// <summary>
    /// 取片交接：机械手伸到位停下后调；只认配了 HandoffOnPick、本轮带取片的上下文、交互中、没有未收尾的动作，不受理返回 null
    /// </summary>
    public ModuleOperation? HandoffPick(TransferContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (OperationGate)
        {
            if (!HandoffOnPick || !context.HasPick || !CanHandoff(context))
            {
                return null;
            }

            return HandoffPickCore(context);
        }
    }

    /// <summary>
    /// 放片交接：机械手伸到位停下后调；只认配了 HandoffOnPlace、本轮带放片的上下文、交互中、没有未收尾的动作，不受理返回 null
    /// </summary>
    public ModuleOperation? HandoffPlace(TransferContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (OperationGate)
        {
            if (!HandoffOnPlace || !context.HasPlace || !CanHandoff(context))
            {
                return null;
            }

            return HandoffPlaceCore(context);
        }
    }

    /// <summary>
    /// 交接的公共条件：同一份上下文、交互中、没有未收尾的动作（调用方已在锁内）
    /// </summary>
    private bool CanHandoff(TransferContext context)
    {
        return ReferenceEquals(_transferContext, context)
            && State == TransferModuleState.Transferring
            && !HasUnsettledOperation();
    }

    /// <summary>
    /// 机械手和站点传输完成
    /// </summary>
    public bool TransferComplete(TransferContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (OperationGate)
        {
            if (!ReferenceEquals(_transferContext, context)
                || HasUnsettledOperation()
                || !TransferStep(TransferModuleState.Transferring, TransferModuleState.TransferComplete))
            {
                return false;
            }

            OnTransferFinished(context);
            return true;
        }
    }

    /// <summary>
    /// 撤回本轮
    /// </summary>
    /// <returns></returns>
    public bool CancelTransfer(TransferContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (OperationGate)
        {
            int state = State;
            if (!ReferenceEquals(_transferContext, context) || state == StandbyState)
            {
                return true;
            }

            if (HasUnsettledOperation()
                || (state != TransferModuleState.PreTransfer && state != TransferModuleState.TransferReady))
            {
                return false;
            }

            OnTransferCancelled(context, state);
            return true;
        }
    }

    #endregion

    #region IStationTaskExecutor 实现

    /// <summary>
    /// 支持的任务：默认只能取放（LoadPort 这类）。有站内任务的站点重写，比如腔体加上工艺。
    /// </summary>
    public virtual IReadOnlyList<string> SupportedTasks => StationTaskAction.PickPlace;

    /// <summary>
    /// 站内任务能不能起：
    /// </summary>
    public virtual HandleResult CheckTask(StationTaskRequest request)
    {
        return HandleResult.Fail(ErrorCodes.StationTaskUnsupported, Name, request.Kind);
    }

    /// <summary>
    /// 起站内任务：默认没有站内任务，返回 null。
    /// </summary>
    public virtual ModuleOperation? StartTask(StationTaskRequest request)
    {
        return null;
    }

    #endregion

    #region 钩子（子类扩展点）

    /// <summary>
    /// 准备一扩展点；公共入口已经在锁内检查准入并记录本轮上下文
    /// </summary>
    protected virtual ModuleOperation? PrepareTransferCore(TransferContext context)
    {
        return TransferStep(StandbyState, TransferModuleState.PreTransfer)
            ? new NoOpOperation("PrepareTransfer")
            : null;
    }

    /// <summary>
    /// 准备二扩展点；公共入口统一检查本轮上下文、状态和当前操作
    /// </summary>
    protected virtual ModuleOperation? PrepareTransfer2Core(TransferContext context)
    {
        return TransferStep(TransferModuleState.PreTransfer, TransferModuleState.TransferReady)
            ? new NoOpOperation("PrepareTransfer2")
            : null;
    }

    /// <summary>
    /// 换段准备扩展点：取片、放片要的机构位置不一样的站点重写，默认直接成功
    /// </summary>
    protected virtual ModuleOperation? PrepareNextLegCore(TransferContext context)
    {
        return new NoOpOperation("PrepareNextLeg");
    }

    /// <summary>
    /// 取片交接扩展点：配了 HandoffOnPick 的站点重写，起动作把片交到手上（比如降顶针），做完仍停在交互中；默认没有交接动作，被拒
    /// </summary>
    protected virtual ModuleOperation? HandoffPickCore(TransferContext context)
    {
        return null;
    }

    /// <summary>
    /// 放片交接扩展点：配了 HandoffOnPlace 的站点重写，起动作把片从手上接走（比如升顶针），做完仍停在交互中；默认被拒
    /// </summary>
    protected virtual ModuleOperation? HandoffPlaceCore(TransferContext context)
    {
        return null;
    }

    /// <summary>
    /// 一轮传片完成后的收尾钩子。默认没有收尾动作，直接回待命态。
    /// 子类有机构收尾时重写，动作真做完了再落待命态；工艺由站内任务入口另行启动。
    /// </summary>
    protected virtual void OnTransferFinished(TransferContext context)
    {
        TransferStep(TransferModuleState.TransferComplete, StandbyState);
    }

    /// <summary>
    /// 撤回本轮的钩子（from 是撤之前的状态）。默认准备阶段什么都没动过，直接回待命态；
    /// 准备时开过门、抽过气的站点重写成先收回去，收好了再自己落待命态。
    /// </summary>
    protected virtual void OnTransferCancelled(TransferContext context, int from)
    {
        TransferStep(from, StandbyState);
    }

    #endregion

    #region 环标记公共实现

    /// <summary>
    /// 状态环单步：锁内校验当前态 → 迁移 → 立即发布；状态不符返回 false。
    /// 子类收尾完毕落状态也走这个，别直接赋 State（会跳过校验和发布）。
    /// </summary>
    protected bool TransferStep(int expected, int next)
    {
        lock (OperationGate)
        {
            if (State != expected)
            {
                return false;
            }

            State = next;
            if (next == StandbyState)
            {
                _transferContext = null;
            }

            PublishState();
            return true;
        }
    }

    /// <summary>
    /// 有还没收尾的动作（准备二、复位这类还在跑），这时不接新的交接步骤
    /// </summary>
    private bool HasUnsettledOperation()
    {
        var operation = CurrentOperation;
        return operation is not null && !operation.IsSettled;
    }

    #endregion
}
