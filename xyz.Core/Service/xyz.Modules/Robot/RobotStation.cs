using System.Globalization;
using xyz.Configs.Models;
using xyz.Shared.Dtos;

namespace xyz.Modules;

public sealed record RobotStation(string Name, int Number, RobotDirection Rotation, double Travel)
{
    /// <summary>
    /// 从 sc.xml 的站点节点读一个站点：Number 必配且为正整数，Rotation 不配为 North，Travel 不配为 0。
    /// 配错就抛，装配即失败——sc.xml 配错是现场最常见的问题，报清楚哪个节点哪个值比事后查 NRE 强。
    /// </summary>
    public static RobotStation FromConfig(ModuleConfig node, string path)
    {
        string? Read(string name) => node.Values
            .FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

        string? numberText = Read(nameof(Number));
        if (!int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < 1)
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Number=\"{numberText}\" 不是有效站点号（须为正整数）。");
        }

        var rotation = RobotDirection.North;
        string? rotationText = Read(nameof(Rotation));
        if (!string.IsNullOrWhiteSpace(rotationText)
            && (!Enum.TryParse(rotationText, ignoreCase: true, out rotation) || !Enum.IsDefined(rotation)))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Rotation=\"{rotationText}\" 不是有效方位（North/East/South/West）。");
        }

        double travel = 0;
        string? travelText = Read(nameof(Travel));
        if (!string.IsNullOrWhiteSpace(travelText)
            && (!double.TryParse(travelText, NumberStyles.Float, CultureInfo.InvariantCulture, out travel) || !double.IsFinite(travel)))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Travel=\"{travelText}\" 不是有效数字。");
        }

        return new RobotStation(node.Name, number, rotation, travel);
    }
}
