using System.Xml.Serialization;

namespace xyz.Configs.Models;

/// <summary>
/// EC 配置文件中的 Value 节点，对应 ec.xml 的 Value。
/// </summary>
[XmlType("Value")]
public class EcValueConfig
{
    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("Description")]
    public string? Description { get; set; }

    [XmlAttribute("Format")]
    public string? Format { get; set; }

    [XmlAttribute("Min")]
    public string? Min { get; set; }

    [XmlAttribute("Max")]
    public string? Max { get; set; }

    [XmlAttribute("Value")]
    public string? Value { get; set; }

    [XmlAttribute("Default")]
    public string? Default { get; set; }

    [XmlAttribute("Unit")]
    public string? Unit { get; set; }

    [XmlAttribute("Options")]
    public string? Options { get; set; }
}
