using xyz.Components.Models;

namespace xyz.Components.Interfaces;

/// <summary>
/// E90 片跟踪的设备侧上报口：晶圆账（WaferManager）上一片的建、挪、改、删由账本调这里，EAP 侧据此推进 E90 的片状态和片位状态并上报 Host。
/// 片在哪、做到哪只有账本一份是真的，EAP 侧照着报。实现由 EAP 侧提供并挂到 WaferManager.E90Callback；没接 EAP 时为 null，照常记账。
/// 全部回调在账本的 EAP 派发线程上按发生顺序串行调用（不占改账的线程——搬运、工艺都在扫描线程上改账）：实现里可以慢，但不要死等。
/// 给的都是那一刻的快照。
/// </summary>
public interface IE90Callback
{
    /// <summary>建了一片（Mapping 落账、人工建片）。</summary>
    void WaferCreated(WaferInfo wafer);

    /// <summary>挪了一片：wafer 已经在新位置，fromModule / fromSlot 是原来的位置。</summary>
    void WaferMoved(WaferInfo wafer, string fromModule, int fromSlot);

    /// <summary>一片的信息变了（片号、批次、载具、工艺状态）。</summary>
    void WaferUpdated(WaferInfo wafer);

    /// <summary>删了一片（载具拿走、重新 Mapping、人工删片）：wafer 是删之前的样子。</summary>
    void WaferDeleted(WaferInfo wafer);
}
