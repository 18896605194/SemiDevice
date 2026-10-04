namespace xyz.Components.Enums;

/// <summary>
/// 双作用气缸在哪一侧。
/// </summary>
public enum TwoStatePosition
{
    /// <summary>
    /// 未知：命令发了、那一侧的到位信号还没亮（还在走）；或者上电两个线圈都没通、两侧信号都没亮（或都亮）；或者 PLC 没连上。
    /// </summary>
    Unknown,

    /// <summary>开到位（门开、Bowl 升、Lift 升）。</summary>
    Opened,

    /// <summary>关到位（门关、Bowl 降、Lift 降）。</summary>
    Closed,
}
