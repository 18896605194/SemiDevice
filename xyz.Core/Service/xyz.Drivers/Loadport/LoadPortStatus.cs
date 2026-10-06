namespace xyz.Drivers.Loadport;

/// <summary>
/// LoadPort 标准状态快照（E87 载具管理语义，厂商无关）：
/// </summary>
public class LoadPortStatus
{
    /// <summary>
    /// 在位
    /// </summary>
    public bool IsPresent { get; init; }

    /// <summary>
    /// 放平
    /// </summary>
    public bool IsPlaced { get; init; }

    /// <summary>
    /// FOUP 锁定（dock/lock 到位）。
    /// </summary>
    public bool IsLocked { get; init; }

    /// <summary>
    /// 门开到位。
    /// </summary>
    public bool IsDoorOpen { get; init; }

    /// <summary>
    /// 门关到位。
    /// </summary>
    public bool IsDoorClosed { get; init; }

    /// <summary>
    /// Table 推进到位（dock）。
    /// </summary>
    public bool IsTableIn { get; init; }

    /// <summary>
    /// Table 退出到位（undock）。
    /// </summary>
    public bool IsTableOut { get; init; }

    /// <summary>
    /// 设备硬件报警位。
    /// </summary>
    public bool IsDeviceAlarm { get; init; }

    /// <summary>
    /// 设备自己报的自动模式（E84 online）；false 视为手动。跟模块的 IsAutoMode（软件里的存取方式）不是一回事。
    /// </summary>
    public bool IsDeviceAutoMode { get; init; }

    /// <summary>
    /// 品牌状态串原文（诊断用，未识别的位都在里面）。
    /// </summary>
    public string Raw { get; init; } = string.Empty;
}
