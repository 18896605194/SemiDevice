using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ToolBuilder.Core;

/// <summary>
/// 元件改名的连带影响。后端很多东西按元件全路径（带 Type 的节点名逐级拼，不带 Type 的分组节点不进路径）或模块名记着：
/// ec.xml 里现场调过的值、EC/SV/报警/事件编号、数据曲线的列名、机械手站点表、IO 点表的 Module/Component 列、
/// machine.xml 里别处写的路径（如数据曲线的 Exclude）。machine.xml 里改了名字，这些都不会跟着改。
/// 打开、保存时记下每个节点的名字作为原名；校验和保存前把改过名的元件和受影响的地方列出来。
/// </summary>
public static class RenameImpact
{
    /// <summary>节点上次打开 / 保存时的名字。新加、复制出来的节点没有它，不算改名。</summary>
    private sealed record OriginalName(string Value);

    public sealed record Rename(XElement Node, string OldPath, string NewPath, IReadOnlyList<string> Impacts);

    /// <summary>编号表跟 ec.xml、machine.xml 在同一个目录，行的 Name 是"元件全路径.成员名"。</summary>
    private static readonly (string File, string Kind)[] NumberTables =
    [
        ("EcDefinitions.xml", "ECID"), ("SvDefinitions.xml", "SVID"), ("AlarmDefinitions.xml", "ALID"), ("EventDefinitions.xml", "CEID"),
    ];

    /// <summary>跟后端数据曲线认 IO 绑定的规则一致：名字里带 Di/Do/Ai/Ao、以 Index 结尾，-1 表示没接线。</summary>
    private static readonly Regex BindingPattern = new("^[A-Za-z]*?(Di|Do|Ai|Ao)[A-Za-z0-9]*Index$", RegexOptions.Compiled);

    /// <summary>打开、保存后调：把每个节点现在的名字记成原名。</summary>
    public static void Remember(ConfigDocument document)
    {
        foreach (var node in document.Settings)
        {
            node.RemoveAnnotations<OriginalName>();
            node.AddAnnotation(new OriginalName(ConfigDocument.Attribute(node, "Name")));
        }
    }

    /// <summary>改过名的元件（带 Type 的节点）和各自的连带影响。只改大小写不算（后端按名字找模块不分大小写）。</summary>
    public static IReadOnlyList<Rename> Find(ConfigDocument document)
    {
        var renames = new List<Rename>();
        foreach (var node in document.Settings)
        {
            if (node.Annotation<OriginalName>() is not { } original || ConfigDocument.Attribute(node, "Type").Length == 0
                || original.Value.Equals(ConfigDocument.Attribute(node, "Name"), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var oldPath = ElementPath(node, original: true);
            renames.Add(new Rename(node, oldPath, ElementPath(node), Describe(document, node, original.Value, oldPath)));
        }

        return renames;
    }

    /// <summary>保存前确认用的说明。</summary>
    public static string Summary(IReadOnlyList<Rename> renames) =>
        $"这次保存改了 {renames.Count} 个元件名，下面这些不会跟着改：\n\n"
        + string.Join("\n\n", renames.Select(r => $"{r.OldPath} → {r.NewPath}\n" + string.Join("\n", r.Impacts.Select(i => "  · " + i))))
        + "\n\n确定保存吗？选“否”回去继续改。";

    /// <summary>后端的元件全路径：从根往下只拼带 Type 的节点名，跟 ElementLoader 一致。</summary>
    public static string ElementPath(XElement node, bool original = false) => string.Join(".", node.AncestorsAndSelf("Setting").Reverse()
        .Where(n => ConfigDocument.Attribute(n, "Type").Length > 0)
        .Select(n => original && n.Annotation<OriginalName>() is { } name ? name.Value : ConfigDocument.Attribute(n, "Name")));

    private static List<string> Describe(ConfigDocument document, XElement node, string oldName, string oldPath)
    {
        var impacts = new List<string>();
        var directory = Path.GetDirectoryName(document.FilePath)!;

        // ec.xml：按元件全路径逐级嵌套，旧名字下的值不会搬到新名字下
        var ec = Load(Path.Combine(directory, "ec.xml"));
        if (ec is null)
        {
            impacts.Add("ec.xml：这个目录下没有 ec.xml（源码里的 machine.xml 旁边本来就没有），机台上现场调过的 EC 值按旧名字记着，改名后不会带过去。");
        }
        else if (FindSetting(ec.Root, oldPath) is { } ecNode && ecNode.Descendants("Value").ToList() is { Count: > 0 } values)
        {
            int tuned = values.Count(v => (string?)v.Attribute("Default") is { } fallback && (string?)v.Attribute("Value") != fallback);
            impacts.Add($"ec.xml：旧名字下有 {values.Count} 个 EC 值{(tuned > 0 ? $"（{tuned} 个现场调过）" : "")}，"
                + "后端会按新名字重新生成默认值，旧值留在 ec.xml 里不会带过去，要手动搬。");
        }

        // 编号表：旧路径下的编号停用，新路径重新分号
        var numbered = new List<string>();
        int svCount = 0;
        bool tablesMissing = false;
        foreach (var (file, kind) in NumberTables)
        {
            if (Load(Path.Combine(directory, file))?.Root is not { } table)
            {
                tablesMissing = true;
                continue;
            }

            int count = table.Elements().Count(row => (string?)row.Attribute("Enabled") != "false"
                && ((string?)row.Attribute("Name") ?? "").StartsWith(oldPath + ".", StringComparison.OrdinalIgnoreCase));
            if (count > 0)
            {
                numbered.Add($"{count} 个 {kind}");
            }

            if (kind == "SVID")
            {
                svCount = count;
            }
        }

        if (numbered.Count > 0)
        {
            impacts.Add($"编号：{string.Join("、", numbered)} 按旧路径分配，后端启动时会停用旧编号、分新编号，EAP 那边对的编号会变。");
        }
        else if (tablesMissing)
        {
            impacts.Add("编号：EC/SV/报警/事件编号按元件全路径分配（编号表跟 ec.xml 在一起，这里没找到），改名后会换新编号，EAP 那边对的编号会变。");
        }

        // 数据曲线：列名 = 元件全路径.属性名（SV）/ 元件全路径.配置项名去掉 Index（IO）
        int bindings = node.DescendantsAndSelf("Setting").SelectMany(n => n.Elements("Value")).Count(v =>
            BindingPattern.IsMatch(ConfigDocument.Attribute(v, "Name"))
            && int.TryParse(ConfigDocument.Attribute(v, "Value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 0);
        if (bindings > 0 || svCount > 0 || tablesMissing)
        {
            var signals = new[] { bindings > 0 ? $"{bindings} 个 IO 点" : null, svCount > 0 ? $"{svCount} 个 SV" : null }.OfType<string>().ToList();
            impacts.Add($"数据曲线：列名按元件全路径生成{(signals.Count > 0 ? $"（这里有 {string.Join("、", signals)}）" : "")}，"
                + "改名后从新列开始记，改名前的历史留在旧列名下，曲线接不上。");
        }

        // 机械手示教点：节点名就是它去的站点（模块）名（机械手下 Size12 / Size8 里的.TeachPointElement）
        foreach (var point in document.Settings
                     .Where(n => ConfigDocument.Attribute(n, "Type").EndsWith(".TeachPointElement", StringComparison.Ordinal))
                     .Where(s => ConfigDocument.Attribute(s, "Name").Equals(oldName, StringComparison.OrdinalIgnoreCase)))
        {
            impacts.Add($"机械手示教点：{ConfigDocument.PathOf(point)} 还叫 {oldName}。示教点名要跟模块名一致，不跟着改的话机械手到不了这个站点。");
        }

        // IO 点表：IO 页按 Module 列分组，Component 列标是哪个设备（绑定按点号，不受影响）
        bool isModule = !oldPath.Contains('.');
        string module = oldPath.Split('.')[0];
        string ioDirectory = IoPointReader.DirectoryFor(document);
        var rows = new List<string>();
        foreach (var kind in new[] { "DI", "DO", "AI", "AO" })
        {
            List<IoPointChoice> points;
            try
            {
                points = IoPointReader.Read(ioDirectory, kind);
            }
            catch (Exception)
            {
                continue;  // 没有点表或读不了：只是少一条提示
            }

            int count = points.Count(p => isModule
                ? p.Module.Equals(oldName, StringComparison.OrdinalIgnoreCase)
                : p.Module.Equals(module, StringComparison.OrdinalIgnoreCase) && p.Component.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (count > 0)
            {
                rows.Add($"{kind}.csv {count} 行");
            }
        }

        if (rows.Count > 0)
        {
            impacts.Add($"IO 点表：{string.Join("、", rows)}的 {(isModule ? "Module" : "Component")} 列还写着 {oldName}，"
                + "IO 页按它分组显示（绑定按点号，不受影响）；仿真器那份点表也一样。");
        }

        // machine.xml 里别处写的旧路径（如数据曲线的 Exclude，逗号分隔、可写上级路径）
        var references = document.Settings.SelectMany(n => n.Elements("Value"))
            .Where(v => ConfigDocument.Attribute(v, "Value")
                .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(token => token.Equals(oldPath, StringComparison.OrdinalIgnoreCase)
                              || token.StartsWith(oldPath + ".", StringComparison.OrdinalIgnoreCase)))
            .Select(v => ConfigDocument.PathOf(v.Parent!) + "." + ConfigDocument.Attribute(v, "Name"))
            .ToList();
        if (references.Count > 0)
        {
            impacts.Add($"machine.xml：还有 {references.Count} 处写着旧路径：{string.Join("、", references)}。");
        }

        return impacts;
    }

    /// <summary>ec.xml 里按路径逐级找 Setting 节点。</summary>
    private static XElement? FindSetting(XElement? root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            current = current?.Elements("Setting").FirstOrDefault(s =>
                ((string?)s.Attribute("Name") ?? "").Equals(segment, StringComparison.OrdinalIgnoreCase));
        }

        return current;
    }

    /// <summary>读旁边的 xml；没有或读不了返回 null（只影响提示的详细程度，不影响保存）。</summary>
    private static XDocument? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader);
        }
        catch (Exception exception) when (exception is IOException or XmlException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
