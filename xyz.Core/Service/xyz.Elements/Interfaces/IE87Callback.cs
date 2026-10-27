using xyz.Drivers.Loadport;

namespace xyz.Elements.Interfaces;

/// <summary>
/// E87 载具管理的设备侧上报口
/// </summary>
public interface IE87Callback
{
    void CarrierArrived(ILoadPortAccess port);

    void CarrierRemoved(ILoadPortAccess port, string? carrierId);

    void CarrierIdRead(ILoadPortAccess port, string carrierId);

    void CarrierIdReadFailed(ILoadPortAccess port);

    void SlotMapRead(ILoadPortAccess port, IReadOnlyList<SlotState> slotMap);

    void LoadCompleted(ILoadPortAccess port);

    void UnloadCompleted(ILoadPortAccess port);

    void AutoModeChanged(ILoadPortAccess port, bool autoMode);

    /// <summary>
    /// 可以开始取放这个载具了（Load 完成后）
    /// </summary>
    /// <param name="port"></param>
    void AccessStarted(ILoadPortAccess port);

    /// <summary>
    /// 不再取放这个载具（Unload 完成或中断）
    /// </summary>
    /// <param name="port"></param>
    void AccessStopped(ILoadPortAccess port);

    /// <summary>
    /// 载具的活干完了
    /// </summary>
    /// <param name="port"></param>
    void CarrierComplete(ILoadPortAccess port);

    /// <summary>
    /// 端口出错
    /// </summary>
    /// <param name="port"></param>
    /// <param name="error"></param>
    void PortError(ILoadPortAccess port, string error);
}
