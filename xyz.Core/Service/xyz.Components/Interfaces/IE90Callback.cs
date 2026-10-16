using xyz.Components.Models;

namespace xyz.Components.Interfaces;

public interface IE90Callback
{
    void WaferCreated(WaferInfo wafer);

    void WaferMoved(WaferInfo wafer, string fromModule, int fromSlot);

    void WaferUpdated(WaferInfo wafer);

    void WaferDeleted(WaferInfo wafer);
}
