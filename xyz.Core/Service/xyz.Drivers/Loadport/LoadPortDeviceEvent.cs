namespace xyz.Drivers.Loadport;


public enum LoadPortDeviceEventKind
{
    Unknown = 0,

    /// <summary>
    /// FOUP 放上。
    /// </summary>
    PodPresent,

    /// <summary>
    /// FOUP 拿走。
    /// </summary>
    PodRemoved,

    /// <summary>
    /// 设备面板访问按钮。
    /// </summary>
    AccessButton,

    /// <summary>
    /// 设备硬件报警。
    /// </summary>
    DeviceAlarm,
}

public sealed class LoadPortDeviceEvent
{
    public LoadPortDeviceEventKind Kind { get; set; }

    /// <summary>
    /// 厂商私有事件名，如 PODON。
    /// </summary>
    public string VendorEventName { get; set; } = string.Empty;

    /// <summary>
    /// 事件附带内容（跟在事件名 '/' 之后），无则为空。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 原始帧（已去壳），供诊断。
    /// </summary>
    public string RawFrame { get; set; } = string.Empty;

    /// <summary>
    /// 收到时刻（本地时间）。
    /// </summary>
    public DateTime ReceivedAt { get; set; }
}
