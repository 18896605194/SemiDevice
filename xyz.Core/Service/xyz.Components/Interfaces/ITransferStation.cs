using xyz.Components.Components;

namespace xyz.Components.Interfaces;

/// <summary>
/// 机械手可服务工位的搬运交互契约。
/// </summary>
public interface ITransferStation : IModule
{
    /// <summary>
    /// 工位槽位数；LoadPort 对应载具的容量。
    /// </summary>
    int SlotCount { get; }

    bool CanPrepare { get; }

    /// <summary>
    /// 准备阶段一：粗准备（腔体排气/预热/机构到位等）
    /// </summary>
    /// <returns></returns>
    ModuleOperation? PrepareTransfer();

    /// <summary>
    /// 准备阶段二
    /// </summary>
    /// <returns></returns>
    ModuleOperation? PrepareTransfer2();

    /// <summary>
    /// 标记本轮取放片进行中
    /// </summary>
    bool Transferring();

    /// <summary>
    /// 标记本轮完成：先落 TransferComplete 并发布，随即回落待命态，并回调 OnTransferFinished。
    /// 状态不符返回 false。
    /// </summary>
    bool TransferComplete();

    /// <summary>
    /// 撤回本轮：准备做了（PreTransfer / TransferReady）但机械手还没伸手
    /// </summary>
    /// <returns></returns>
    bool CancelTransfer();
}
