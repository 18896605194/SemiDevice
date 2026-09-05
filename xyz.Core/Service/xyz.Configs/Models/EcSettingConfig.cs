using System.Xml.Serialization;

namespace xyz.Configs.Models;

/// <summary>
/// EC 配置文件中的 Setting 节点，对应 ec.xml 的 Setting。
/// </summary>
[XmlType("Setting")]
public class EcSettingConfig
{
    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("Value")]
    public List<EcValueConfig> Values { get; set; } = new();

    [XmlElement("Setting")]
    public List<EcSettingConfig> Children { get; set; } = new();
}
