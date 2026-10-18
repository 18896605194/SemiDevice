using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 机械手可服务工位基类
/// </summary>
public abstract class BaseTransferStationModule : BaseModule, ITransferStation, IStationTaskExecutor
{
    #region 待命态（传片环的起点和终点）

    /// <summary>
    /// 待命态：站点空着、机械手可以来取放片的状态，传片环从这里出发、做完回到这里。
    /// 各站点不一样：腔体是 Idle，LoadPort 是 Loaded（载具装好了才能取放）。
    /// </summary>
    protected virtual int StandbyState => ModuleState.Idle;

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

    /// <summary>准备一扩展点；公共入口已经在锁内检查准入并记录本轮上下文。</summary>
    protected virtual ModuleOperation? PrepareTransferCore(TransferContext context)
    {
        return TransferStep(StandbyState, TransferModuleState.PreTransfer)
            ? new NoOpOperation("PrepareTransfer")
            : null;
    }

    /// <summary>准备二扩展点；公共入口统一检查本轮上下文、状态和当前操作。</summary>
    protected virtual ModuleOperation? PrepareTransfer2Core(TransferContext context)
    {
        return TransferStep(TransferModuleState.PreTransfer, TransferModuleState.TransferReady)
            ? new NoOpOperation("PrepareTransfer2")
            : null;
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
