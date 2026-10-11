using System.Text.RegularExpressions;

namespace xyz.Modules;

/// <summary>
/// 下拉字段的数据源（sc.xml 字段节点的 Source），两种写法：
/// 直接写选项，英文逗号隔开（如 <c>Time,Scan</c>）；
/// 或者 <c>Devices:类型[.属性][@字段名]</c>：腔体里装的这种设备（按类名认，基类名也算）的名字，写了属性就取属性的值；
/// 写了 @字段名，只在这一步那个字段选中的设备下面找（如 <c>Devices:NozzleComponent.Chemical@Arm</c>：所选摆臂上喷嘴的药液）。
/// 写法不对开机就抛（报清楚哪个节点），不留到编辑配方时才发现。
/// </summary>
public sealed class ProcessRecipeSource
{
    /// <summary>
    /// 从腔体设备取选项的写法开头。
    /// </summary>
    public const string DevicesPrefix = "Devices:";

    /// <summary>
    /// 类型、属性、字段名都是代码里的名字：字母开头，只用字母、数字、下划线。
    /// </summary>
    private static readonly Regex NameRule = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private ProcessRecipeSource(string text, IReadOnlyList<string> options, string deviceType, string property, string parentKey)
    {
        Text = text;
        Options = options;
        DeviceType = deviceType;
        Property = property;
        ParentKey = parentKey;
    }

    /// <summary>
    /// sc.xml 里写的原文（日志、报错里用）。
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// 直接写的选项，按写的先后；从设备取的为空。
    /// </summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>
    /// 设备类型（类名）；直接写选项的为空。
    /// </summary>
    public string DeviceType { get; }

    /// <summary>
    /// 取设备的哪个属性；空 = 取设备名。
    /// </summary>
    public string Property { get; }

    /// <summary>
    /// @ 后面的字段名：只在这一步那个字段选中的设备下面找；空 = 整个腔体里找。
    /// </summary>
    public string ParentKey { get; }

    public bool IsDevices => DeviceType.Length > 0;

    /// <summary>
    /// 解析 sc.xml 里写的数据源；写法不对就抛，path 是字段节点的路径（报错用）。
    /// </summary>
    public static ProcessRecipeSource Parse(string? text, string path)
    {
        string source = (text ?? string.Empty).Trim();
        if (source.Length == 0)
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 是下拉，要配 Source（选项用逗号隔开，或 Devices:类型[.属性][@字段名]）");
        }

        if (!source.StartsWith(DevicesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var options = new List<string>();
            foreach (string option in source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (options.Contains(option, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"sc.xml 节点 {path} 的 Source=\"{source}\" 里选项 {option} 写了两遍");
                }

                options.Add(option);
            }

            if (options.Count == 0)
            {
                throw new InvalidOperationException($"sc.xml 节点 {path} 的 Source=\"{source}\" 一个选项都没有");
            }

            return new ProcessRecipeSource(source, options, string.Empty, string.Empty, string.Empty);
        }

        string spec = source[DevicesPrefix.Length..].Trim();
        string parentKey = string.Empty;
        int at = spec.IndexOf('@');
        if (at >= 0)
        {
            parentKey = spec[(at + 1)..].Trim();
            spec = spec[..at].Trim();
        }

        string deviceType = spec;
        string property = string.Empty;
        int dot = spec.IndexOf('.');
        if (dot >= 0)
        {
            deviceType = spec[..dot].Trim();
            property = spec[(dot + 1)..].Trim();
        }

        bool parentOk = at < 0 || NameRule.IsMatch(parentKey);
        bool propertyOk = dot < 0 || NameRule.IsMatch(property);
        if (!NameRule.IsMatch(deviceType) || !propertyOk || !parentOk)
        {
            throw new InvalidOperationException(
                $"sc.xml 节点 {path} 的 Source=\"{source}\" 写法不对：应为 Devices:类型[.属性][@字段名]，如 Devices:NozzleComponent.Chemical@Arm");
        }

        return new ProcessRecipeSource(source, [], deviceType, property, parentKey);
    }
}
