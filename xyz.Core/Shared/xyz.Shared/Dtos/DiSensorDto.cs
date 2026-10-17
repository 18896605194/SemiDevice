namespace xyz.Shared.Dtos;

/// <summary>
/// 一个触发 / 没触发的信号：DI 信号（DiSensorComponent），或按液位计读数比出来的 AI 液位信号（AiLevelComponent）
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
