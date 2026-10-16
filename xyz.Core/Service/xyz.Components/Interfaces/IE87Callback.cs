using xyz.Drivers.Loadport;

namespace xyz.Components.Interfaces;

/// <summary>
/// E87 载具管理的设备侧上报口
/// </summary>
public interface IE87Callback
{
    void CarrierArrived(ILoadPort port);

    void CarrierRemoved(ILoadPort port, string? carrierId);

    void CarrierIdRead(ILoadPort port, string carrierId);

    void CarrierIdReadFailed(ILoadPort port);

    void SlotMapRead(ILoadPort port, IReadOnlyList<SlotState> slotMap);

    void LoadCompleted(ILoadPort port);

    void UnloadCompleted(ILoadPort port);

    void AutoModeChanged(ILoadPort port, bool autoMode);

    /// <summary>
    /// 可以开始取放这个载具了（Load 完成后）
    /// </summary>
    /// <param name="port"></param>
    void AccessStarted(ILoadPort port);

    /// <summary>
    /// 不再取放这个载具（Unload 完成或中断）
    /// </summary>
    /// <param name="port"></param>
    void AccessStopped(ILoadPort port);

    /// <summary>
    /// 载具的活干完了
    /// </summary>
    /// <param name="port"></param>
    void CarrierComplete(ILoadPort port);

    /// <summary>
    /// 端口出错
    /// </summary>
    /// <param name="port"></param>
    /// <param name="error"></param>
    void PortError(ILoadPort port, string error);
}
