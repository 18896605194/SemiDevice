using System.Xml.Serialization;

namespace xyz.Configs.Models;

/// <summary>
/// EC 配置文件根对象，对应 ec.xml 的 ArrayOfSetting。
/// </summary>
[XmlRoot("ArrayOfSetting")]
public class EcConfig
{
    [XmlElement("Setting")]
    public List<EcSettingConfig> Settings { get; set; } = new();
}
