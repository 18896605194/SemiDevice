using System.Xml.Serialization;

namespace xyz.Components.Collectors;

/// <summary>
/// EcDefinitions.xml 的一行：ECID + EC 全名（组件全路径.属性名）+ 元数据。EC 的值不在这里，在 ec.xml。
/// </summary>
public sealed class EcDefinition : IDefinitionRow
{
    [XmlAttribute("ECID")]
    public int Id { get; set; }

    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 代码里还有这一项为 True；删掉了置 False，号保留。
    /// </summary>
    [XmlAttribute("Enabled")]
    public bool Enabled { get; set; } = true;

    [XmlAttribute("Format")]
    public string Format { get; set; } = string.Empty;

    [XmlAttribute("Unit")]
    public string Unit { get; set; } = string.Empty;

    [XmlAttribute("Min")]
    public string Min { get; set; } = string.Empty;

    [XmlAttribute("Max")]
    public string Max { get; set; } = string.Empty;

    [XmlAttribute("Default")]
    public string Default { get; set; } = string.Empty;

    [XmlAttribute("Options")]
    public string Options { get; set; } = string.Empty;

    [XmlAttribute("Description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否上传 EAP：新行取 [VariableMark] 的 Visible，之后以表里为准（现场改 False 即不上传）。
    /// </summary>
    [XmlAttribute("Visible")]
    public bool Visible { get; set; } = true;
}

/// <summary>
/// EcDefinitions.xml 根节点。
/// </summary>
[XmlRoot("EcDefinitions")]
public sealed class EcDefinitionFile : IDefinitionFile<EcDefinition>
{
    [XmlElement("Ec")]
    public List<EcDefinition> Items { get; set; } = new();
}
