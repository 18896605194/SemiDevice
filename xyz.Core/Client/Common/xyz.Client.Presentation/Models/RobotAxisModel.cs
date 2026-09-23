namespace xyz.Client.Presentation.Models;

/// <summary>
/// 机械手单个轴的坐标显示数据，轴位表按行显示（轴名 + 坐标）。
/// </summary>
public class RobotAxisModel
{
    /// <summary>
    /// 轴名（轴表里的名字，如 X / Z / Theta / Arm1）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 当前坐标。
    /// </summary>
    public double Position { get; set; }
}
