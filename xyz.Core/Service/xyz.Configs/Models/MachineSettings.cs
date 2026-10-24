using System.Xml.Serialization;

namespace xyz.Configs.Models;

/// <summary>
/// machine.xml 配置文件根对象。
/// </summary>
[XmlRoot("ArrayOfSetting")]
public class MachineSettings
{
    [XmlElement("Setting")]
    public List<ModuleConfig> Modules { get; set; } = new();
}
