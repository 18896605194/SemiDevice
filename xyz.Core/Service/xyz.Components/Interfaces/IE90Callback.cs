using xyz.Components.Models;

namespace xyz.Components.Interfaces;

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
