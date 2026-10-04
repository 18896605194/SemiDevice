using System.Globalization;

namespace xyz.Shared.Dtos;

/// <summary>
/// 一个模块下的手动部件和它们的实时数据（腔体手动页的轴页签、气缸表、三维图都从这里取）：
/// 部件是 sc.xml 里标了种类（[PartKind]）的组件，数据是组件上标了 [LiveValue] 的属性。
/// 后端不认具体是什么硬件，界面按 Kind 选模板、按数据名取值；sc 里加了组件（Arm2……）这里自动多一项，推送不用改。
/// 有变化才推、留存（token 是模块名）；跟模块状态分开推：摆臂一动就要推，别让只关心模块状态的界面跟着刷。
/// </summary>
public class ModulePartsDto
{
    /// <summary>模块实例名，与 EventBus token 一致，如 "Chamber1"。</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>部件，按 sc.xml 里的先后（先父后子）。</summary>
    public List<PartDto> Parts { get; set; } = [];

    /// <summary>按全路径找部件（忽略大小写）；没有返回 null。</summary>
    public PartDto? Find(string path)
    {
        return Parts.FirstOrDefault(part => string.Equals(part.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>跟上一次推的比有没有变化（部件组成或任何一个值）；没有上一次视为变化。</summary>
    public bool HasStateChanged(ModulePartsDto? previous)
    {
        if (previous is null || Module != previous.Module || Parts.Count != previous.Parts.Count)
        {
            return true;
        }

        for (int i = 0; i < Parts.Count; i++)
        {
            if (!PartDto.Same(Parts[i], previous.Parts[i]))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 一个手动部件：组件全路径、种类、组件类名和实时数据。
/// </summary>
public class PartDto
{
    /// <summary>组件全路径（sc.xml 的组件路径），如 "Chamber1.Arm1.Lift"；手动动作按它找部件。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>部件种类（组件类上的 [PartKind]）：如 "Axis" 轴、"TwoState" 双作用气缸、"OneState" 阀；界面按它选放哪、长什么样。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>组件类名，如 "ArmAxisComponent"、"SpinMotorComponent"：三维图据此认摆臂和旋转电机。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>实时数据：属性名 → 值（不变区域性的字符串；布尔 True / False，枚举写名字）。</summary>
    public Dictionary<string, string> Values { get; set; } = [];

    /// <summary>取一个值的原文；没有这一项给空串。</summary>
    public string Get(string name)
    {
        return Values.TryGetValue(name, out var value) ? value : string.Empty;
    }

    /// <summary>取数值；没有这一项或不是数给 null。</summary>
    public double? GetDouble(string name)
    {
        return double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }

    /// <summary>取布尔；没有这一项或不是 True / False 给 false。</summary>
    public bool GetBool(string name)
    {
        return bool.TryParse(Get(name), out bool value) && value;
    }

    /// <summary>两个快照是否一样：路径、种类、类名和每个值都一样。</summary>
    public static bool Same(PartDto left, PartDto right)
    {
        if (left.Path != right.Path || left.Kind != right.Kind || left.Type != right.Type || left.Values.Count != right.Values.Count)
        {
            return false;
        }

        foreach (var (name, value) in left.Values)
        {
            if (!right.Values.TryGetValue(name, out var other) || other != value)
            {
                return false;
            }
        }

        return true;
    }
}
