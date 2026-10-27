using xyz.Elements.Models;

namespace xyz.Elements.Interfaces;

public interface IE90Callback
{
    void WaferCreated(WaferInfo wafer);

    void WaferMoved(WaferInfo wafer, string fromModule, int fromSlot);

    void WaferUpdated(WaferInfo wafer);

    void WaferDeleted(WaferInfo wafer);
}
