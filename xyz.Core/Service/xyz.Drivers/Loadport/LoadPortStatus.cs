namespace xyz.Drivers.Loadport;

/// <summary>
/// LoadPort 标准状态快照（E87 载具管理语义，厂商无关）：
/// </summary>
public class LoadPortStatus
{
    /// <summary>
    /// 在位
    /// </summary>
    public bool PodPresent { get; init; }

    /// <summary>
    /// 放平
    /// </summary>
    public bool PodPlaced { get; init; }

    /// <summary>
    /// FOUP 锁定（dock/lock 到位）。
    /// </summary>
    public bool CarrierLocked { get; init; }

    /// <summary>
    /// 门开到位。
    /// </summary>
    public bool DoorOpen { get; init; }

    /// <summary>
    /// 门关到位。
    /// </summary>
    public bool DoorClosed { get; init; }

    /// <summary>
    /// Table 推进到位（dock）。
    /// </summary>
    public bool TableIn { get; init; }

    /// <summary>
    /// Table 退出到位（undock）。
    /// </summary>
    public bool TableOut { get; init; }

    /// <summary>
    /// 设备硬件报警位。
    /// </summary>
    public bool DeviceAlarm { get; init; }

    /// <summary>
    /// 自动模式（E84 online）；false 视为手动。
    /// </summary>
    public bool AutoMode { get; init; }

    /// <summary>
    /// 品牌状态串原文（诊断用，未识别的位都在里面）。
    /// </summary>
    public string Raw { get; init; } = string.Empty;
}
