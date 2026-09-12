using xyz.Modules.Enums;

namespace xyz.Modules;

/// <summary>
/// 机械手可服务工位基类：实现 ITransferStation 的标准交互环（锚点态 → TransferReady →
/// Transferring → TransferComplete → 回锚点态），承载环标记与完成钩子的公共实现。
/// LoadPort、工艺腔等被机械手服务的模块继承本类，锚点态与准备动作各自重写。
/// </summary>
public abstract class BaseTransferStationModule : BaseModule, ITransferStation
{
    /// <summary>
    /// 锚点态：工位就绪、可被机械手服务的状态（LoadPort=Loaded；腔体按自己的状态表定义）。子类重写。
    /// </summary>
    protected virtual int AnchorState => ModuleState.Idle;

    /// <summary>
    /// 工位当前状态码（SV）；本基类的环标记与查表都依赖它，子类以真实 SV 覆写。
    /// </summary>
    public abstract int State { get; protected set; }

    /// <inheritdoc />
    public virtual bool CanPrepare => State == AnchorState;

    /// <summary>
    /// 准备阶段一默认实现：无设备动作，允许即返回立即成功的空操作；需要粗准备（腔体）的子类重写。
    /// </summary>
    public virtual ModuleOperation? PrepareTransfer()
    {
        return CanPrepare ? new NoOpOperation("PrepareTransfer") : null;
    }

    /// <summary>
    /// 准备阶段二默认实现：状态环锚点态 → TransferReady 的纯标记；需要开门等设备动作的子类重写。
    /// </summary>
    public virtual ModuleOperation? FinalizeTransfer()
    {
        return MarkTransferReady() ? new NoOpOperation("FinalizeTransfer") : null;
    }

    /// <summary>
    /// 锚点态 → TransferReady（准备二的默认落点）；流程直接驱动状态环时用。
    /// </summary>
    public bool MarkTransferReady()
    {
        return MarkTransferStep(AnchorState, TransferModuleState.TransferReady);
    }

    /// <inheritdoc />
    public bool MarkTransferring()
    {
        return MarkTransferStep(TransferModuleState.TransferReady, TransferModuleState.Transferring);
    }

    /// <inheritdoc />
    public bool MarkTransferComplete()
    {
        lock (OperationGate)
        {
            if (State != TransferModuleState.Transferring)
            {
                return false;
            }

            State = TransferModuleState.TransferComplete;
            PublishState();

            State = AnchorState;
            PublishState();

            OnTransferFinished();
            return true;
        }
    }

    /// <summary>
    /// 一轮传片完成后的处理钩子（腔体：关门、触发工艺；LoadPort：默认无动作）。
    /// 在 MarkTransferComplete 回落锚点态后回调。
    /// </summary>
    protected virtual void OnTransferFinished()
    {
    }

    /// <summary>
    /// 状态发布钩子：环标记改状态后立即调用，让变化第一时间可见（不等扫描周期）；
    /// 子类按自己的 DTO 发布（含变化检测），默认空实现。
    /// </summary>
    protected virtual void PublishState()
    {
    }

    private bool MarkTransferStep(int expected, int next)
    {
        lock (OperationGate)
        {
            if (State != expected)
            {
                return false;
            }

            State = next;
            PublishState();
            return true;
        }
    }
}
