using xyz.Modules.Enums;

namespace xyz.Modules;

/// <summary>
/// 机械手可服务工位基类：实现 ITransferStation 的标准交互环
/// （锚点态 → TransferReady → Transferring → TransferComplete → 回锚点态）。
/// </summary>
public abstract class BaseTransferStationModule : BaseModule, ITransferStation
{
    #region 锚点与状态（环的起终点定义）

    /// <summary>
    /// 锚点态：工位就绪、可被机械手服务的状态（LoadPort=Loaded；腔体按自己的状态表定义）。子类重写。
    /// </summary>
    protected virtual int AnchorState => ModuleState.Idle;

    /// <summary>
    /// 工位当前状态码（SV）；环标记与查表都依赖它，子类以真实 SV 覆写。
    /// </summary>
    public abstract int State { get; protected set; }

    #endregion

    #region 契约实现（ITransferStation：调度/机械手流程调用的五个方法）

    public virtual bool CanPrepare => State == AnchorState;

    /// <summary>
    /// 准备一（粗准备）默认实现：无设备动作，允许即返回立即成功的空操作；需要粗准备的子类（腔体）重写。
    /// </summary>
    public virtual ModuleOperation? PrepareTransfer()
    {
        return CanPrepare ? new NoOpOperation("PrepareTransfer") : null;
    }

    public virtual ModuleOperation? FinalizeTransfer()
    {
        return MarkTransferStep(AnchorState, TransferModuleState.TransferReady)
            ? new NoOpOperation("FinalizeTransfer")
            : null;
    }

    /// <summary>
    /// 标记本轮取放片进行中（机械手开始交互时调用）；状态不符返回 false。
    /// </summary>
    public bool MarkTransferring()
    {
        return MarkTransferStep(TransferModuleState.TransferReady, TransferModuleState.Transferring);
    }

    /// <summary>
    /// 标记本轮完成：先落 TransferComplete 并发布，随即回落锚点态，并回调 OnTransferFinished。
    /// 状态不符返回 false。
    /// </summary>
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

    #endregion

    #region 钩子（子类扩展点）

    /// <summary>
    /// 一轮传片完成后的处理钩子（腔体：关门、触发工艺；默认无动作）。
    /// 在 MarkTransferComplete 回落锚点态后回调。
    /// </summary>
    protected virtual void OnTransferFinished()
    {
    }

    /// <summary>
    /// 状态发布钩子：环标记改状态后立即调用（不等扫描周期）；子类按自己的 DTO 发布，默认空。
    /// </summary>
    protected virtual void PublishState()
    {
    }

    #endregion

    #region 环标记公共实现

    /// <summary>
    /// 状态环单步：锁内校验当前态 → 迁移 → 立即发布；状态不符返回 false。
    /// </summary>
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

    #endregion
}
