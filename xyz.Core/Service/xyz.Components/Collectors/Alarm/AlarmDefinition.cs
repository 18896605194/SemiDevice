using System.Xml.Serialization;

namespace xyz.Components.Collectors;

/// <summary>
/// AlarmDefinitions.xml 的一行：ALID + 报警全名（组件全路径.报警代码）+ [Alarm] 上的文本、分类、等级等。
/// </summary>
public sealed class AlarmDefinition : IDefinitionRow
{
    [XmlAttribute("ALID")]
    public int Id { get; set; }

    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 代码里还有这一项为 True；删掉了置 False，号保留。
    /// </summary>
    [XmlAttribute("Enabled")]
    public bool Enabled { get; set; } = true;

    [XmlAttribute("Category")]
    public string Category { get; set; } = string.Empty;

    [XmlAttribute("AlarmLevel")]
    public string AlarmLevel { get; set; } = string.Empty;

    /// <summary>
    /// 报警文本（SECS 的 ALTX）。
    /// </summary>
    [XmlAttribute("ALTX")]
    public string AlarmText { get; set; } = string.Empty;

    [XmlAttribute("Description")]
    public string Description { get; set; } = string.Empty;

    [XmlAttribute("Solution")]
    public string Solution { get; set; } = string.Empty;

    /// <summary>
    /// 报出事件的 CEID；Warn 级、已停用或事件编号不可用时为 0。
    /// </summary>
    [XmlAttribute("SetEventId")]
    public int SetEventId { get; set; }

    /// <summary>
    /// 清除事件的 CEID；Warn 级、已停用或事件编号不可用时为 0。
    /// </summary>
    [XmlAttribute("ClearEventId")]
    public int ClearEventId { get; set; }
}

/// <summary>
/// AlarmDefinitions.xml 根节点。
/// </summary>
[XmlRoot("AlarmDefinitions")]
public sealed class AlarmDefinitionFile : IDefinitionFile<AlarmDefinition>
{
    [XmlElement("Alarm")]
    public List<AlarmDefinition> Items { get; set; } = new();
}
