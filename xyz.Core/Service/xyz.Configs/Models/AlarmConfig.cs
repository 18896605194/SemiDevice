using System.Xml.Serialization;

namespace xyz.Configs.Models;

/// <summary>
/// machine.xml 配置中的 Alarm 节点。
/// </summary>
[XmlType("Alarm")]
public class AlarmConfig
{
    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("Level")]
    public string? Level { get; set; }
}
