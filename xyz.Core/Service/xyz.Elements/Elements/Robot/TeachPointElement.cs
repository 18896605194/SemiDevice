using System.Globalization;
using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;
using xyz.Configs.Models;
using xyz.Shared.Dtos;

namespace xyz.Elements.Elements;

/// <summary>
/// 机械手示教点：挂在机械手的 Size12 / Size8 分组下，节点名就是它去的站点（模块）名；
/// 纯数据，开机读好就不变，不扫描、不动硬件
/// </summary>
[Element(description: "机械手示教点（站号、伸出方向、伸出距离、取放能用的手）")]
public class TeachPointElement : ElementBase, ITeachPoint
{
    #region Machine

    [Machine("0", "TeachPoint", "控制器里的站号（取放指令发的就是它），必配、正整数", Required = true)]
    public int Number { get; set; }

    [Machine("North", "TeachPoint", "伸出方向：North / East / South / West，界面画机械手朝哪转")]
    public RobotDirection Direction { get; set; } = RobotDirection.North;

    [Machine("0", "TeachPoint", "伸出距离，界面画手臂伸多长")]
    public double Y { get; set; }

    [Machine("", "TeachPoint", "从这个站取片能用的手指号，英文逗号隔开（如 1,2）；空 = 所有手指都能用")]
    public string PickArms { get; set; } = string.Empty;

    [Machine("", "TeachPoint", "往这个站放片能用的手指号，英文逗号隔开（如 1,2）；空 = 所有手指都能用")]
    public string PlaceArms { get; set; } = string.Empty;

    #endregion

    /// <summary>
    /// 从这个站取片能用的手指号（PickArms 拆开），开机读好就不变
    /// </summary>
    public IReadOnlyList<int> PickArmNumbers { get; private set; } = [];

    /// <summary>
    /// 往这个站放片能用的手指号（PlaceArms 拆开），开机读好就不变
    /// </summary>
    public IReadOnlyList<int> PlaceArmNumbers { get; private set; } = [];

    public bool AllowsPick(int arm)
    {
        return Allows(PickArmNumbers, arm);
    }

    public bool AllowsPlace(int arm)
    {
        return Allows(PlaceArmNumbers, arm);
    }

    /// <summary>
    /// 配错开机就报：站号、方向、伸出距离、手指号哪个不对，报出节点和值
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (Number < 1)
        {
            throw new InvalidOperationException($"machine.xml 节点 {FullPath} 的 Number={Number} 不是有效站号（必配，正整数）。");
        }

        if (!Enum.IsDefined(Direction))
        {
            throw new InvalidOperationException($"machine.xml 节点 {FullPath} 的 Direction={Direction} 不是有效伸出方向（North / East / South / West）。");
        }

        if (!double.IsFinite(Y))
        {
            throw new InvalidOperationException($"machine.xml 节点 {FullPath} 的 Y={Y} 不是有效伸出距离（要是有限的数）。");
        }

        PickArmNumbers = ParseArms(PickArms, nameof(PickArms));
        PlaceArmNumbers = ParseArms(PlaceArms, nameof(PlaceArms));
    }

    /// <summary>
    /// 拆一项手指号：英文逗号隔开的正整数，去重、升序；空着是所有手指都能用
    /// </summary>
    private List<int> ParseArms(string text, string name)
    {
        var arms = new List<int>();
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int arm) || arm < 1)
            {
                throw new InvalidOperationException(
                    $"machine.xml 节点 {FullPath} 的 {name}=\"{text}\" 里 \"{part}\" 不是有效手指号（正整数，英文逗号隔开，如 1,2）。");
            }

            if (!arms.Contains(arm))
            {
                arms.Add(arm);
            }
        }

        arms.Sort();
        return arms;
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
}
