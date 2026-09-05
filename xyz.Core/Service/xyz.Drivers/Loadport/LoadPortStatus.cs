namespace xyz.Drivers.Loadport;

/// <summary>
/// LoadPort 标准状态快照（E87 载具管理语义，厂商无关）：
/// 各品牌协议（如 FCD 的 GET:STATE）归一化到这里，模块/EAP/界面只认本类型。
/// 未映射的位保留在 Raw 原文中，待各品牌协议手册确认后补充。
/// </summary>
public class LoadPortStatus
{
    /// <summary>
    /// FOUP 在位（E87 Carrier Presence）。
    /// </summary>
    public bool PodPresent { get; init; }

    /// <summary>
    /// FOUP 放置到位（在位且放好，E87 Placed）。
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
