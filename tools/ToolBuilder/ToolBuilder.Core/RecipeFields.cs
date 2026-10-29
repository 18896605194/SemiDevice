using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ToolBuilder.Core;

/// <summary>
/// 工艺配方的两张字段表：ProcessRecipe 元件下的 Head（配方头，一个子节点一项）和 Fields（步骤，一个子节点一个字段 = 工艺配方页上的一列），
/// 节点名就是字段名。步骤的下拉按腔体的 Type 分开写选项：Source.A 是 Type 为 A 的腔体那一套，腔体没配 Type 的写 Source。
/// 规则跟后端 RecipeElement 开机时的检查一样（后端配错了起不来），"配方头"、"配方字段"页和校验共用。
/// </summary>
public static class RecipeFields
{
    public const string ElementType = "xyz.Elements.Elements.RecipeElement";
    public const string NodeName = "Fields";
    public const string HeadNodeName = "Head";

    /// <summary>步骤时长那个字段：必须有、类型是小数，"配方字段"页上锁着。</summary>
    public const string SecondsKey = "Seconds";

    /// <summary>下拉选项的参数名；按 Type 分开的写成 Source.类型。</summary>
    public const string SourceName = "Source";

    /// <summary>配方头的 Type：选项是腔体配的 Type，不用写。</summary>
    public const string TypeKey = "Type";

    /// <summary>腔体的基类：它下面的 &lt;Value Name="Type" /&gt; 就是站点类型，配方按它分。</summary>
    private const string ChamberBaseType = "xyz.Modules.BaseChamberModule";

    /// <summary>类型：machine.xml 里的写法 → 页面上显示的 C# 类型（下拉是一个选项集合，显示 collection）。</summary>
    public static readonly IReadOnlyList<RecipeFieldType> Types =
    [
        new("Int", "int"), new("Double", "double"), new("Choice", "collection"), new("Bool", "bool"), new("Text", "string"),
    ];

    /// <summary>配方头固定的几项（跟后端 ProcessRecipeHead 一样）：字段名 → 类型。必须有，字段名、类型不能改。</summary>
    public static readonly IReadOnlyDictionary<string, string> HeadFixed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Index"] = "Int", ["Name"] = "Text", [TypeKey] = "Choice", ["Timeout"] = "Double", ["Description"] = "Text",
        ["CreatedBy"] = "Text", ["CreatedAt"] = "Text", ["ModifiedBy"] = "Text", ["ModifiedAt"] = "Text", ["Revision"] = "Int",
    };

    /// <summary>配方头里程序算的那几项：页面上只显示，"值从哪来"一栏写清楚怎么来的。</summary>
    private static readonly IReadOnlyDictionary<string, string> HeadAuto = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Index"] = "自动：库里的编号", ["Name"] = "自动：新建、重命名时填", ["CreatedBy"] = "自动：新建时的登录人", ["CreatedAt"] = "自动：新建时间",
        ["ModifiedBy"] = "自动：保存时的登录人", ["ModifiedAt"] = "自动：保存时间", ["Revision"] = "自动：每存一次加 1",
    };

    private static readonly Regex KeyRule = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private const NumberStyles IntegerStyle = NumberStyles.AllowLeadingSign;
    private const NumberStyles DecimalStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static bool IsRecipe(XElement? node) => node is not null && ConfigDocument.Attribute(node, "Type") == ElementType;

    /// <summary>这个节点是工艺配方元件下的步骤字段表。</summary>
    public static bool IsFieldsNode(XElement? node) => IsChildTable(node, NodeName);

    /// <summary>这个节点是工艺配方元件下的配方头字段表。</summary>
    public static bool IsHeadNode(XElement? node) => IsChildTable(node, HeadNodeName);

    private static bool IsChildTable(XElement? node, string name) => node is not null
        && ConfigDocument.Attribute(node, "Name").Equals(name, StringComparison.OrdinalIgnoreCase) && IsRecipe(node.Parent);

    public static bool IsNumber(string type) => type.Equals("Int", StringComparison.OrdinalIgnoreCase) || type.Equals("Double", StringComparison.OrdinalIgnoreCase);

    public static bool IsHeadFixed(string key) => HeadFixed.ContainsKey(key);

    public static bool IsHeadAuto(string key) => HeadAuto.ContainsKey(key);

    /// <summary>配方头里程序算的那一项是怎么来的；不是程序算的为空。</summary>
    public static string HeadAutoText(string key) => HeadAuto.GetValueOrDefault(key) ?? "";

    /// <summary>这种 Type 的选项写在哪个参数：没配 Type 的是 Source，配了的是 Source.类型。</summary>
    public static string SourceKey(string stationType) => stationType.Trim().Length == 0 ? SourceName : $"{SourceName}.{stationType.Trim()}";

    /// <summary>是不是写下拉选项的参数（Source、Source.类型）。</summary>
    public static bool IsSourceKey(string name) => name.Equals(SourceName, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(SourceName + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 腔体配的几种 Type（按 machine.xml 里的先后，不分大小写去重；没配的是空字符串），跟后端 Bind 时认的一样：
    /// 元件是腔体（元件来源里认得出基类）的节点下的 &lt;Value Name="Type" /&gt;；元件来源里没有这个类的，有这个参数就算。没有腔体时只有空字符串一种。
    /// </summary>
    public static IReadOnlyList<string> StationTypes(ConfigDocument document, ElementCatalog catalog)
    {
        var types = new List<string>();
        foreach (var node in document.Settings)
        {
            var componentType = ConfigDocument.Attribute(node, "Type");
            if (componentType.Length == 0 || IsRecipe(node)) continue;
            var spec = catalog.Elements.GetValueOrDefault(componentType);
            var written = ConfigDocument.Value(node, TypeKey);
            bool chamber = spec is not null ? spec.BaseTypes?.Contains(ChamberBaseType) == true : written is not null;
            if (!chamber) continue;
            var type = (written ?? "").Trim();
            if (!types.Contains(type, StringComparer.OrdinalIgnoreCase)) types.Add(type);
        }
        if (types.Count == 0) types.Add("");
        return types;
    }

    /// <summary>
    /// 查步骤字段表，每条问题挂在对应的字段节点上：字段名、中文名、类型、上下限、小数位、必填、默认值，
    /// 下拉按腔体的每种 Type 都要写选项、默认值在每一套里；必须有 Seconds（小数）；用不上的参数（比如下拉配了单位）给提示。
    /// </summary>
    public static List<ValidationIssue> Check(XElement fields, IReadOnlyList<string> stationTypes)
    {
        var issues = new List<ValidationIssue>();
        var nodes = fields.Elements("Setting").ToList();
        var seconds = nodes.FirstOrDefault(n => ConfigDocument.Attribute(n, "Name").Equals(SecondsKey, StringComparison.OrdinalIgnoreCase));
        if (seconds is null) issues.Add(Issue("错误", fields, $"字段表里要有 {SecondsKey}（步骤时长，类型 Double）：合计时长和超时时间靠它。"));
        else if (!Read(seconds, "Type").Equals("Double", StringComparison.OrdinalIgnoreCase)) issues.Add(Issue("错误", seconds, $"{SecondsKey} 是步骤时长，类型要是 Double。"));
        foreach (var node in nodes) CheckField(node, stationTypes, issues);
        return issues;
    }

    /// <summary>
    /// 查配方头字段表：固定的几项都要有、类型跟固定的一样；自己加的项跟步骤字段一样查（下拉只写一套 Source）。
    /// Type 的选项是腔体配的，不用写；程序算的那几项不查范围、默认值。
    /// </summary>
    public static List<ValidationIssue> CheckHead(XElement head)
    {
        var issues = new List<ValidationIssue>();
        var nodes = head.Elements("Setting").ToList();
        foreach (var pair in HeadFixed)
        {
            var node = nodes.FirstOrDefault(n => ConfigDocument.Attribute(n, "Name").Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (node is null) issues.Add(Issue("错误", head, $"配方头要有 {pair.Key}（固定的一项，类型 {pair.Value}）。"));
            else if (!Read(node, "Type").Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue("错误", node, $"{pair.Key} 是配方头固定的一项，类型要是 {pair.Value}。"));
        }
        foreach (var node in nodes)
        {
            var key = ConfigDocument.Attribute(node, "Name");
            if (IsHeadAuto(key) || key.Equals(TypeKey, StringComparison.OrdinalIgnoreCase)) CheckName(node, issues);
            else CheckField(node, [""], issues);
        }
        return issues;
    }

    private static void CheckField(XElement node, IReadOnlyList<string> stationTypes, List<ValidationIssue> issues)
    {
        void Error(string message) => issues.Add(Issue("错误", node, message));
        void Warn(string message) => issues.Add(Issue("警告", node, message));
        CheckName(node, issues);
        var type = Read(node, "Type");
        if (!Types.Any(t => t.Code.Equals(type, StringComparison.OrdinalIgnoreCase)))
        {
            Error($"类型 {type} 不对：只能是 Int、Double、Choice、Bool、Text。");
            return;
        }
        var number = IsNumber(type);
        var isDouble = type.Equals("Double", StringComparison.OrdinalIgnoreCase);
        var isChoice = type.Equals("Choice", StringComparison.OrdinalIgnoreCase);
        foreach (var name in new[] { "Unit", "Min", "Max" })
            if (!number && Read(node, name).Length > 0) Warn($"{name} 只对整数、小数有用，{type} 用不上，后端会忽略。");
        if (!isDouble && Read(node, "Decimals").Length > 0) Warn("Decimals 只对小数有用，后端会忽略。");
        double? min = number ? Limit(node, "Min", type, Error) : null;
        double? max = number ? Limit(node, "Max", type, Error) : null;
        if (min is not null && max is not null && min.Value > max.Value) Error($"下限 {min.Value} 比上限 {max.Value} 大。");
        int? decimals = null;
        var decimalsText = Read(node, "Decimals");
        if (isDouble && decimalsText.Length > 0)
        {
            if (int.TryParse(decimalsText, NumberStyles.None, CultureInfo.InvariantCulture, out var places)) decimals = places;
            else Error($"小数位 {decimalsText} 要是 0 或正整数。");
        }
        var required = Read(node, "Required");
        if (required.Length > 0 && !bool.TryParse(required, out _)) Error($"必填 Required=\"{required}\" 只能是 true 或 false。");
        var written = node.Elements("Value").Select(v => ConfigDocument.Attribute(v, "Name")).Where(IsSourceKey).ToList();
        if (!isChoice)
        {
            if (written.Count > 0) Warn("选项只对下拉（collection）有用，后端会忽略。");
        }
        else
        {
            foreach (var name in written)
            {
                var writtenType = name.Length > SourceName.Length ? name[(SourceName.Length + 1)..] : "";
                if (!stationTypes.Contains(writtenType.Trim(), StringComparer.OrdinalIgnoreCase))
                    Warn($"{name}：没有腔体的 Type 是 {(writtenType.Length == 0 ? "（空）" : writtenType)}，这一套用不上。");
            }
        }
        var defaultValue = Read(node, "Default");
        if (defaultValue.Length > 0)
        {
            var problem = CheckValue(type, defaultValue, min, max, decimals);
            if (problem is not null) Error($"默认值 {defaultValue} 不合规：{problem}");
        }
        if (!isChoice) return;
        foreach (var stationType in stationTypes)
        {
            var key = SourceKey(stationType);
            var sourceError = CheckSource(Read(node, key), out var options);
            if (sourceError is not null)
            {
                Error($"{key}：{sourceError}");
                continue;
            }
            if (defaultValue.Length > 0 && !options.Contains(defaultValue, StringComparer.OrdinalIgnoreCase))
                Error($"默认值 {defaultValue} 不在 {key} 的选项 {string.Join(",", options)} 里。");
        }
    }

    /// <summary>字段名（字母开头，只用字母、数字、下划线）、中文名、不是元件。</summary>
    private static void CheckName(XElement node, List<ValidationIssue> issues)
    {
        var key = ConfigDocument.Attribute(node, "Name");
        if (ConfigDocument.Attribute(node, "Type").Length > 0) issues.Add(Issue("错误", node, "字段节点不要写 Type：字段表下面一个子节点就是一项，不是元件。"));
        if (!KeyRule.IsMatch(key)) issues.Add(Issue("错误", node, $"字段名 {key} 不行：字母开头，只用字母、数字、下划线（会写成配方文件里的属性名）。"));
        if (Read(node, "Text").Length == 0) issues.Add(Issue("错误", node, "没填中文名（Text），配方页上这一项没名字。"));
    }

    private static ValidationIssue Issue(string severity, XElement node, string message) => new(severity, ConfigDocument.PathOf(node), message);

    /// <summary>
    /// 查一套下拉选项（跟后端一样）：英文逗号隔开、至少一个、不能重复（不分大小写）。选项通过 options 带出来（查默认值用）。没问题返回 null。
    /// 摆臂、药液这类选腔体设备的，写的要跟 machine.xml 里一样（摆臂节点名、喷嘴的 Chemical），这里不管对不对得上，工艺执行时腔体照名字找。
    /// </summary>
    public static string? CheckSource(string source, out IReadOnlyList<string> options)
    {
        options = [];
        var list = new List<string>();
        foreach (var option in SplitOptions(source))
        {
            if (list.Contains(option, StringComparer.OrdinalIgnoreCase)) return $"选项 {option} 写了两遍。";
            list.Add(option);
        }
        options = list;
        return list.Count == 0 ? "下拉要写选项：英文逗号隔开，如 Time,Scan。" : null;
    }

    /// <summary>把格子里的选项按英文逗号拆开（去掉首尾空白和空的），按写的先后。</summary>
    public static IReadOnlyList<string> SplitOptions(string source) =>
        source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>选项连回格子里的写法：去掉首尾空白和空的，英文逗号隔开。</summary>
    public static string JoinOptions(IEnumerable<string> options) =>
        string.Join(",", options.Select(option => option.Trim()).Where(option => option.Length > 0));

    /// <summary>按类型、上下限、小数位查一个值（跟后端一样：不认千分位、科学计数法）；没问题返回 null。</summary>
    public static string? CheckValue(string type, string value, double? min, double? max, int? decimals)
    {
        value = value.Trim();
        if (type.Equals("Bool", StringComparison.OrdinalIgnoreCase))
            return bool.TryParse(value, out _) ? null : "开关只能是 true 或 false。";
        if (!IsNumber(type)) return null;
        double number;
        if (type.Equals("Int", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(value, IntegerStyle, CultureInfo.InvariantCulture, out var integer)) return "要是整数。";
            number = integer;
        }
        else
        {
            if (!decimal.TryParse(value, DecimalStyle, CultureInfo.InvariantCulture, out var parsed)) return "要是数字。";
            var dot = value.IndexOf('.');
            var places = dot < 0 ? 0 : value[(dot + 1)..].TrimEnd('0').Length;
            if (decimals is not null && places > decimals.Value) return $"最多 {decimals.Value} 位小数。";
            number = (double)parsed;
        }
        if (min is not null && number < min.Value) return $"不能小于下限 {min.Value}。";
        if (max is not null && number > max.Value) return $"不能大于上限 {max.Value}。";
        return null;
    }

    private static double? Limit(XElement node, string name, string type, Action<string> error)
    {
        var text = Read(node, name);
        if (text.Length == 0) return null;
        if (!double.TryParse(text, DecimalStyle, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        {
            error($"{name}={text} 不是数。");
            return null;
        }
        if (type.Equals("Int", StringComparison.OrdinalIgnoreCase) && value != Math.Floor(value))
        {
            error($"整数字段的 {name}={text} 也要是整数。");
            return null;
        }
        return value;
    }

    private static string Read(XElement node, string name) => (ConfigDocument.Value(node, name) ?? "").Trim();
}

/// <summary>字段类型：machine.xml 里的写法和页面上显示的 C# 类型。</summary>
public sealed record RecipeFieldType(string Code, string Label);
