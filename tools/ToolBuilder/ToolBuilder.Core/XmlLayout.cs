using System.Xml.Linq;

namespace ToolBuilder.Core;

/// <summary>
/// 往 machine.xml 里加、删、挪节点时按原文件的缩进摆好。machine.xml 是手工维护、带注释的文件，
/// 工具加进去的节点要跟手写的一样一行一个、按层级缩进，不能挤成一行。
/// 打开时保留了空白（PreserveWhitespace），这里只动新加、挪动、删除的节点前后那段空白，别处原样不碰。
/// 内存里换行一律是 \n（读 XML 时已规整），保存时再换回原文件的换行符。
/// </summary>
public static class XmlLayout
{
    private const string DefaultUnit = "\t";

    /// <summary>
    /// 加一个子节点：before 为空加在最后，否则加在 before（子元素或注释）前面。
    /// 新建的节点（里面还没有空白）连同它的下级一起排好缩进；复制来的节点里面原样保留。
    /// </summary>
    public static void Add(XElement parent, XElement child, XNode? before = null)
    {
        string unit = UnitOf(parent);
        string parentIndent = IndentOf(parent);
        string indent = parentIndent + unit;
        Format(child, indent, unit);
        if (before is not null)
        {
            before.AddBeforeSelf(child, new XText("\n" + indent));
            EnsureLineBefore(child, indent);
            return;
        }

        // 最后一般是收尾那段空白（换行 + 上级缩进），新节点加在它前面。
        if (parent.LastNode is XText tail && IsLineBreak(tail))
        {
            tail.AddBeforeSelf(new XText("\n" + indent), child);
        }
        else
        {
            parent.Add(new XText("\n" + indent), child, new XText("\n" + parentIndent));
        }
    }

    /// <summary>
    /// 加在第一个位置（参数 Value 排在子节点和注释前面）。
    /// </summary>
    public static void AddFirst(XElement parent, XElement child)
    {
        Add(parent, child, parent.Nodes().FirstOrDefault(node => node is not XText));
    }

    /// <summary>
    /// 加在 anchor 后面、跟它同级（复制节点、追加参数用）：跟 anchor 同样缩进，各占一行。
    /// </summary>
    public static void AddAfter(XElement anchor, XElement element)
    {
        string indent = IndentOf(anchor);
        Format(element, indent, UnitOf(anchor));
        anchor.AddAfterSelf(new XText("\n" + indent), element);
    }

    /// <summary>
    /// 加在 anchor 前面、跟它同级（上移节点用）。
    /// </summary>
    public static void AddBefore(XElement anchor, XElement element)
    {
        string indent = IndentOf(anchor);
        Format(element, indent, UnitOf(anchor));
        anchor.AddBeforeSelf(element, new XText("\n" + indent));
        EnsureLineBefore(element, indent);
    }

    /// <summary>
    /// 删掉节点连同它前面那段缩进（换行 + 缩进），不在文件里留下空行。
    /// </summary>
    public static void Remove(XElement element)
    {
        if (element.PreviousNode is XText text && IsLineBreak(text))
        {
            text.Remove();
        }

        element.Remove();
    }

    /// <summary>
    /// 节点所在行的缩进：它前面那段空白里最后一个换行之后的部分；前面没换行（挤在一行里）就按上级缩进 + 一级推算。
    /// </summary>
    public static string IndentOf(XElement element)
    {
        if (element.PreviousNode is XText text && IsLineBreak(text))
        {
            return text.Value[(text.Value.LastIndexOf('\n') + 1)..];
        }

        return element.Parent is null ? string.Empty : IndentOf(element.Parent) + UnitOf(element);
    }

    /// <summary>
    /// 文件的缩进单位：根节点下第一个缩进过的子节点用的那段（machine.xml 是一个 Tab）；没有就用一个 Tab。
    /// </summary>
    public static string UnitOf(XElement element)
    {
        var root = element.AncestorsAndSelf().Last();
        foreach (var child in root.Elements())
        {
            if (child.PreviousNode is XText text && IsLineBreak(text))
            {
                string indent = text.Value[(text.Value.LastIndexOf('\n') + 1)..];
                if (indent.Length > 0)
                {
                    return indent;
                }
            }
        }

        return DefaultUnit;
    }

    /// <summary>
    /// 新建的节点按层级排好：每个子节点一行，结束标签回到本级缩进。里面已经有空白的（复制来的、排过的）不动。
    /// </summary>
    private static void Format(XElement element, string indent, string unit)
    {
        var children = element.Elements().ToList();
        if (children.Count == 0 || element.Nodes().OfType<XText>().Any())
        {
            return;
        }

        string inner = indent + unit;
        foreach (var child in children)
        {
            child.AddBeforeSelf(new XText("\n" + inner));
            Format(child, inner, unit);
        }

        element.Add(new XText("\n" + indent));
    }

    /// <summary>
    /// 插到一行中间（前面紧挨着开始标签或别的节点）时，前面补一个换行。
    /// </summary>
    private static void EnsureLineBefore(XElement element, string indent)
    {
        if (element.PreviousNode is not XText text || !IsLineBreak(text))
        {
            element.AddBeforeSelf(new XText("\n" + indent));
        }
    }

    private static bool IsLineBreak(XText text)
    {
        return text is not XCData && string.IsNullOrWhiteSpace(text.Value) && text.Value.Contains('\n');
    }
}
