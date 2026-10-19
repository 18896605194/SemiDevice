using System.Globalization;
using xyz.Configs.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Models;

/// <summary>
/// 机械手站点表一条：LoadPort / 腔体在 sc.xml _robot.Stations 下各编一条。
/// Number 站点号；Y 机械手伸出距离（数值）；Direction 伸出方向（<see cref="RobotDirection"/>）；
/// PickArms 从这个站取片能用哪几只手、PlaceArms 往这个站放片能用哪几只手（手指号，空 = 所有手指都能用）。
/// </summary>
public sealed record RobotStation(string Name, int Number, RobotDirection Direction, double Y,
    IReadOnlyList<int> PickArms, IReadOnlyList<int> PlaceArms)
{
    /// <summary>
    /// 这只手能不能从本站取片：没限定手指时都能用
    /// </summary>
    public bool AllowsPick(int arm)
    {
        return Allows(PickArms, arm);
    }

    /// <summary>
    /// 这只手能不能往本站放片：没限定手指时都能用
    /// </summary>
    public bool AllowsPlace(int arm)
    {
        return Allows(PlaceArms, arm);
    }

    /// <summary>
    /// 手指号在列表里，或者列表空着（没限定）
    /// </summary>
    private static bool Allows(IReadOnlyList<int> arms, int arm)
    {
        if (arms.Count == 0)
        {
            return true;
        }

        return arms.Contains(arm);
    }

    /// <summary>
    /// 从 sc.xml 的站点节点读一个站点：Number 必配且为正整数，Y 不配为 0，Direction 不配为 North，
    /// PickArms / PlaceArms 不配为所有手指都能用（配了就是逗号分隔的手指号，如 1,2）。
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

        var direction = RobotDirection.North;
        string? directionText = Read(nameof(Direction));
        if (!string.IsNullOrWhiteSpace(directionText)
            && (!Enum.TryParse(directionText, ignoreCase: true, out direction) || !Enum.IsDefined(direction)))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Direction=\"{directionText}\" 不是有效伸出方向（RobotDirection：North/East/South/West）。");
        }

        double y = 0;
        string? yText = Read(nameof(Y));
        if (!string.IsNullOrWhiteSpace(yText)
            && (!double.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !double.IsFinite(y)))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Y=\"{yText}\" 不是有效伸出距离（须为有限数值）。");
        }

        var pickArms = ParseArms(Read(nameof(PickArms)), path, nameof(PickArms));
        var placeArms = ParseArms(Read(nameof(PlaceArms)), path, nameof(PlaceArms));
        return new RobotStation(node.Name, number, direction, y, pickArms, placeArms);
    }

    /// <summary>
    /// 读一项手指号列表：逗号分隔的正整数，去重、升序；没配为空
    /// </summary>
    private static List<int> ParseArms(string? text, string path, string name)
    {
        var arms = new List<int>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return arms;
        }

        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int arm) || arm < 1)
            {
                throw new InvalidOperationException($"sc.xml 节点 {path} 的 {name}=\"{text}\" 里 \"{part}\" 不是有效手指号（须为正整数，逗号分隔，如 1,2）。");
            }

            if (!arms.Contains(arm))
            {
                arms.Add(arm);
            }
        }

        arms.Sort();
        return arms;
    }
}
