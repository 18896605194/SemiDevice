namespace xyz.Modules;

/// <summary>
/// 机械手可服务工位契约：调度/机械手流程只依赖本接口，不感知工位是 LoadPort 还是腔体。
/// 交互标准环见 TransferModuleState：锚点态 → 准备一 → 准备二(TransferReady) → 传片 → 完成 → 回锚点态。
/// </summary>
public interface ITransferStation
{
    /// <summary>
    /// 当前状态是否允许发起准备；调度器选工位时先过滤，避免盲目发起被拒。
    /// </summary>
    bool CanPrepare { get; }

    /// <summary>
    /// 准备阶段一：粗准备（腔体排气/预热/机构到位等）；无需准备的工位为立即成功的空操作。
    /// 返回操作实例；null 表示状态不允许/被拒。
    /// </summary>
    ModuleOperation? PrepareTransfer();

    /// <summary>
    /// 准备阶段二：最终准备（开门/放行），成功后工位进入 TransferReady，机械手可开始取放片。
    /// 返回操作实例；null 表示状态不允许/被拒。
    /// </summary>
    ModuleOperation? FinalizeTransfer();

    /// <summary>
    /// 标记本轮取放片进行中（机械手开始交互时调用）；状态不符返回 false。
    /// </summary>
    bool MarkTransferring();

    /// <summary>
    /// 标记本轮完成：先落 TransferComplete 并发布，随即回落锚点态，并回调 OnTransferFinished。
    /// 状态不符返回 false。
    /// </summary>
    bool MarkTransferComplete();
}
