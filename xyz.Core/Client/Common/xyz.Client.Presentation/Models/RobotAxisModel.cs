using xyz.Client.Presentation.Localization;

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
    /// 当前坐标；还没查到或驱动没连上为 null。
    /// </summary>
    public double? Position { get; set; }

    /// <summary>
    /// 坐标文字：两位小数；没有坐标显示占位符。
    /// </summary>
    public string PositionText
    {
        get
        {
            if (!Position.HasValue)
            {
                return L10n.Get("robotmanual.na");
            }

            return Position.Value.ToString("F2");
        }
    }
}
