using System.Xml.Serialization;

namespace xyz.Configs.Models;

/// <summary>
/// SC 配置文件根对象。
/// </summary>
[XmlRoot("ArrayOfSetting")]
public class ScConfig
{
    [XmlElement("Setting")]
    public List<ModuleConfig> Modules { get; set; } = new();
}
