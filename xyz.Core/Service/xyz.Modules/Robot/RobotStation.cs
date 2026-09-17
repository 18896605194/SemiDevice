using System.Globalization;
using xyz.Configs.Models;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 机械手的一个站点（sc.xml 本 Robot 节点 Stations 下的子节点）：
/// 站点号用于取放片下发；转台方位与平移距离随状态推给界面，显示机械手去哪、朝哪。
/// </summary>
/// <param name="Name">模块名，如 LoadPort1。</param>
/// <param name="Number">设备站点号，正整数。</param>
/// <param name="Rotation">机械手服务该站点时的转台方位。</param>
/// <param name="Travel">机械手服务该站点时的平移距离。</param>
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
