namespace xyz.Drivers.Robot;


public enum RobotDeviceEventKind
{
    Unknown = 0,

    /// <summary>
    /// 手指在位变化。
    /// </summary>
    WaferPresence,

    /// <summary>
    /// 设备主动上报报错。
    /// </summary>
    DeviceError,

    /// <summary>
    /// 心跳。
    /// </summary>
    HeartBeat,
}

public sealed class RobotDeviceEvent
{
    public RobotDeviceEventKind Kind { get; set; }

    /// <summary>
    /// 手指号（WaferPresence 有效）。
    /// </summary>
    public int Arm { get; set; }

    /// <summary>
    /// 手指上是否有片（WaferPresence 有效）。
    /// </summary>
    public bool HasWafer { get; set; }

    /// <summary>
    /// 事件内容（DeviceError 为"错误码#内容"原文），无则为空。
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
