using System.Xml.Serialization;

namespace xyz.Components.Collectors;

/// <summary>
/// DvDefinitions.xml 的一行：DVID + DV 全名 + 格式与说明。DV 只在事件报告时带值（如报警事件里的 ALID、报警文本）。
/// </summary>
public sealed class DvDefinition : IDefinitionRow
{
    [XmlAttribute("DVID")]
    public int Id { get; set; }

    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 还在用为 True；不用了置 False，号保留。
    /// </summary>
    [XmlAttribute("Enabled")]
    public bool Enabled { get; set; } = true;

    [XmlAttribute("Format")]
    public string Format { get; set; } = string.Empty;

    [XmlAttribute("Unit")]
    public string Unit { get; set; } = string.Empty;

    [XmlAttribute("Description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// DvDefinitions.xml 根节点。
/// </summary>
[XmlRoot("DvDefinitions")]
public sealed class DvDefinitionFile : IDefinitionFile<DvDefinition>
{
    [XmlElement("Dv")]
    public List<DvDefinition> Items { get; set; } = new();
}
