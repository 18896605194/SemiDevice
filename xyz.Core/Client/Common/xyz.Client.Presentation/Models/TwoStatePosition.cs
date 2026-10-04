namespace xyz.Client.Presentation.Models;

/// <summary>
/// 双作用气缸在哪一侧（部件推送里气缸的 Position，名字跟后端一致）。
/// </summary>
public enum TwoStatePosition
{
    /// <summary>未知：命令发了、到位信号还没亮（还在走），或者两侧信号都没亮、PLC 没连上。</summary>
    Unknown,

    /// <summary>开到位：门开、Bowl 升、Lift 升。</summary>
    Opened,

    /// <summary>关到位：门关、Bowl 降、Lift 降。</summary>
    Closed,
}
