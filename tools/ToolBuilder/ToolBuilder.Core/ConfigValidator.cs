using System.Globalization;
using System.Xml.Linq;

namespace ToolBuilder.Core;

public sealed record ValidationIssue(string Severity, string Path, string Message);

public static class ConfigValidator
{
    public static List<ValidationIssue> Validate(ConfigDocument document, ElementCatalog catalog)
    {
        var issues = new List<ValidationIssue>();
        void Add(string severity, XElement node, string message) => issues.Add(new(severity, ConfigDocument.PathOf(node), message));
        foreach (var parent in document.Settings.Prepend(document.Xml.Root!))
        {
            foreach (var duplicate in parent.Elements("Setting").GroupBy(x => ConfigDocument.Attribute(x, "Name"), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                Add("错误", parent, $"同级节点名称重复：{duplicate.Key}");
        }
        foreach (var node in document.Settings)
        {
            var name = ConfigDocument.Attribute(node, "Name");
            if (string.IsNullOrWhiteSpace(name)) Add("错误", node, "节点名称不能为空。");
            if (name.Contains('.')) Add("警告", node, "名称包含点号，会使元件路径产生歧义。");
            var order = ConfigDocument.Attribute(node, "InitOrder");
            if (order.Length > 0 && (!int.TryParse(order, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0))
                Add("错误", node, "InitOrder 必须是非负整数；0 表示使用元件默认顺序。");
            foreach (var group in node.Elements("Value").GroupBy(x => ConfigDocument.Attribute(x, "Name"), StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(group.Key)) Add("错误", node, "参数名称不能为空。");
                if (group.Count() > 1) Add("错误", node, $"参数重复：{group.Key}");
            }
            var type = ConfigDocument.Attribute(node, "Type");
            if (type.Length > 0)
            {
                if (!catalog.Elements.TryGetValue(type, out var component)) Add("警告", node, $"元件类型未加载：{type}。原有配置会保留，请补选 DLL 后再校验。");
                else
                {
                    foreach (var property in component.Properties)
                    {
                        var configured = ConfigDocument.Value(node, property.Name);
                        var value = configured ?? property.DefaultValue;
                        var error = CheckValue(property, value);
                        if (error is not null) Add("错误", node, $"{property.Name}：{error}");
                        if (property.Required && configured is null) Add("警告", node, $"{property.Name} 标记为必填，目前使用默认值 {property.DefaultValue}。");
                        if (property.Name.EndsWith("Index", StringComparison.Ordinal) && property.Group == "IO"
                            && int.TryParse(value, out var index) && index < -1) Add("错误", node, $"{property.Name} 索引不能小于 -1。");
                    }
                    foreach (var value in node.Elements("Value"))
                    {
                        var valueName = ConfigDocument.Attribute(value, "Name");
                        if (!component.Properties.Any(p => p.Name.Equals(valueName, StringComparison.OrdinalIgnoreCase)))
                            Add("警告", node, $"{valueName} 没有对应的 [Machine] 属性：后端装配时不认它，记一条错误日志、照常开机（名字写错了，或者参数已经删了）。");
                    }
                }
            }
            // 工艺配方的两张字段表：规则跟后端一样，后端开机时查到错就起不来（步骤下拉按腔体的每种 Type 查）
            if (RecipeFields.IsFieldsNode(node)) issues.AddRange(RecipeFields.Check(node, RecipeFields.StationTypes(document, catalog)));
            if (RecipeFields.IsHeadNode(node)) issues.AddRange(RecipeFields.CheckHead(node));
            if (RecipeFields.IsRecipe(node) && !node.Elements("Setting").Any(child => ConfigDocument.Attribute(child, "Name").Equals(RecipeFields.NodeName, StringComparison.OrdinalIgnoreCase)))
                Add("错误", node, "工艺配方元件下要有 Fields 分组（每一步有哪些字段），后端没有它起不来。");
            if (RecipeFields.IsRecipe(node) && !node.Elements("Setting").Any(child => ConfigDocument.Attribute(child, "Name").Equals(RecipeFields.HeadNodeName, StringComparison.OrdinalIgnoreCase)))
                Add("错误", node, "工艺配方元件下要有 Head 分组（配方头有哪些项），后端没有它起不来。");
        }
        CheckSharedBindings(document, catalog, issues);
        foreach (var rename in RenameImpact.Find(document))
            foreach (var impact in rename.Impacts) Add("警告", rename.Node, $"由 {rename.OldPath} 改名为 {rename.NewPath}，{impact}");
        var rpc = document.Settings.FirstOrDefault(n => ConfigDocument.Attribute(n, "Type") == "xyz.Elements.Elements.RpcElement");
        var hsms = document.Settings.FirstOrDefault(n => ConfigDocument.Attribute(n, "Type") == "xyz.Elements.Elements.HsmsElement");
        if (rpc is not null && hsms is not null && ConfigDocument.Value(hsms, "IsEnable")?.Equals("True", StringComparison.OrdinalIgnoreCase) == true
            && ConfigDocument.Value(hsms, "IsActive")?.Equals("True", StringComparison.OrdinalIgnoreCase) != true
            && (ConfigDocument.Value(rpc, "Port") ?? "5000") == (ConfigDocument.Value(hsms, "Port") ?? "5000"))
            Add("警告", hsms, "HSMS 被动监听与 gRPC 使用相同端口，请检查监听地址是否冲突。");
        foreach (var diagnostic in catalog.Diagnostics) issues.Add(new("警告", "元件目录", diagnostic));
        return issues;
    }

    public static string? CheckValue(PropertySpec property, string value)
    {
        value = property.NormalizeValue(value);
        if (property.Required && string.IsNullOrWhiteSpace(value)) return "不能为空。";
        if (property.Choices.Count > 0)
            return property.Choices.Any(c => c.Equals(value, StringComparison.OrdinalIgnoreCase)) ? null : $"可选值：{string.Join(", ", property.Choices)}";
        return property.Kind switch
        {
            "System.Int32" when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) => "须为 32 位整数。",
            "System.Double" when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d) => "须为有限数值，使用小数点。",
            "System.String" or "System.Int32" or "System.Double" or "System.Boolean" => null,
            _ => "当前后端加载器不支持此属性类型。"
        };
    }

    /// <summary>
    /// 全机的 IO 点和轴的 PLC 数据块不能两个元件共用，复制腔体、照抄节点时最容易忘了改：
    /// 输出点（DO/AO）重复会让两个元件抢同一个输出；轴命令块（SendPlcDataPath）重复，后端启动时直接报「轴命令块路径重复」起不来——这两类算错误。
    /// 输入点（DI/AI）、轴反馈块重复可能是有意共用，只提示。没写的项按代码默认值算（IO 索引默认 -1，不占点）。
    /// </summary>
    private static void CheckSharedBindings(ConfigDocument document, ElementCatalog catalog, List<ValidationIssue> issues)
    {
        var bindings = new List<(string Kind, string Key, XElement Node, string Property)>();
        foreach (var node in document.Settings)
        {
            var specs = catalog.Elements.GetValueOrDefault(ConfigDocument.Attribute(node, "Type"))?.Properties ?? [];
            var names = node.Elements("Value").Select(v => ConfigDocument.Attribute(v, "Name"))
                .Concat(specs.Select(p => p.Name)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var value = (ConfigDocument.Value(node, name)
                    ?? specs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.DefaultValue ?? "").Trim();
                if (IoPointReader.KindFor(name) is { } kind)
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0)
                        bindings.Add((kind, index.ToString(CultureInfo.InvariantCulture), node, name));
                }
                else if (name.EndsWith("PlcDataPath", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    bindings.Add((name, value, node, name));
            }
        }
        foreach (var group in bindings.GroupBy(b => (Kind: b.Kind.ToUpperInvariant(), b.Key)).Where(g => g.Count() > 1))
        {
            var (kind, key) = group.Key;
            var owners = string.Join("、", group.Select(b => ConfigDocument.PathOf(b.Node) + "." + b.Property));
            var (severity, message) = kind switch
            {
                "DO" or "AO" => ("错误", $"{kind} {key} 被多个元件同时使用：{owners}。会抢同一个输出，复制节点后请改成各自的点号。"),
                "DI" or "AI" => ("警告", $"{kind} {key} 被多处同时读取：{owners}。请确认是有意共用。"),
                "SENDPLCDATAPATH" => ("错误", $"轴命令块 {key} 被多个轴同时使用：{owners}。后端启动时会报「轴命令块路径重复」起不来。"),
                _ => ("警告", $"PLC 数据块 {key} 被多个轴同时使用：{owners}。请确认是有意共用。"),
            };
            foreach (var node in group.Select(b => b.Node).Distinct()) issues.Add(new(severity, ConfigDocument.PathOf(node), message));
        }
    }
}
