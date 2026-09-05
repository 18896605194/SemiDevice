using System.Xml.Serialization;

namespace xyz.Configs.Models;

/// <summary>
/// SC 配置中的 Value 节点。
/// </summary>
[XmlType("Value")]
public class ValueConfig
{
    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("Value")]
    public string? Value { get; set; }

    [XmlAttribute("Description")]
    public string? Description { get; set; }
}
