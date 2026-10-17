namespace xyz.Shared.Dtos;

/// <summary>
/// 一个 DI 信号（DiSensorComponent）
/// </summary>
public class DiSensorDto
{
    /// <summary>
    /// sc 里的节点名，原样显示
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 有没有触发（已按触发值、防抖算好）；读不到是 null
    /// </summary>
    public bool? IsActive { get; set; }
}
