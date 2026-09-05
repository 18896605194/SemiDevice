using System.Xml.Serialization;

namespace xyz.Configs.Models;

/// <summary>
/// SC 配置中的 Setting 节点（模块）。
/// </summary>
[XmlType("Setting")]
public class ModuleConfig
{
    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("Type")]
    public string? Type { get; set; }

    [XmlAttribute("InitOrder")]
    public int InitOrder { get; set; }

    [XmlAttribute("Role")]
    public string? Role { get; set; }

    [XmlElement("Value")]
    public List<ValueConfig> Values { get; set; } = new();

    [XmlElement("Alarm")]
    public List<AlarmConfig> Alarms { get; set; } = new();

    [XmlElement("Setting")]
    public List<ModuleConfig> Children { get; set; } = new();
}
