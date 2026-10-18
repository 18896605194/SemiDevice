using xyz.Components.Components;
using xyz.Shared.Dtos;

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

    /// <summary>
    /// 工位当前能否开始一次搬运准备；准备完成前不代表机械手可以伸入。
    /// </summary>
    bool CanPrepareTransfer { get; }

    /// <summary>
    /// 只查询，不占用工位。null 表示允许，否则返回拒绝原因（错误码和参数）；发起准备时仍须重新检查。
    /// 自动调度所需的启用、在线状态及具体槽位、机械手资源由调用方另行检查。
    /// </summary>
    HandleResult? CheckPrepareTransfer();

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
    /// 接受本轮完成通知：落 TransferComplete 并发布，再交给站点收尾；默认同步回待命态。
    /// 状态不符返回 false；true 表示通知已接受，不代表机型扩展的异步收尾已完成。
    /// </summary>
    bool TransferComplete();

    /// <summary>
    /// 撤回本轮：准备做了（PreTransfer / TransferReady）但机械手还没伸手。
    /// true 表示站点接受撤回（默认同步回待命态）；false 时调用方须保留本轮占用。
    /// </summary>
    /// <returns></returns>
    bool CancelTransfer();
}
