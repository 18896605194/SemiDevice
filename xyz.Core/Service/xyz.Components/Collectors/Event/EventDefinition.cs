using System.Xml.Serialization;

namespace xyz.Components.Collectors;

/// <summary>
/// EventDefinitions.xml 的一行：CEID + 事件全名 + 文本与描述，报告时带的 DV 列在 Payload 子节点里。
/// 两种来源：组件上的 [EventAttribut]（全名 = 组件全路径.事件代码）；报警的报出/清除（System.Alarm.{ALID}.Set/Clear）。
/// </summary>
public sealed class EventDefinition : IDefinitionRow
{
    [XmlAttribute("CEID")]
    public int Id { get; set; }

    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 代码里还有这一项为 True；删掉了置 False，号保留。
    /// </summary>
    [XmlAttribute("Enabled")]
    public bool Enabled { get; set; } = true;

    [XmlAttribute("Text")]
    public string EventText { get; set; } = string.Empty;

    [XmlAttribute("Description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 事件报告时带的 DV（引用 DvDefinitions.xml 的 DVID），按顺序。
    /// </summary>
    [XmlElement("Payload")]
    public List<EventPayload> Payloads { get; set; } = new();
}

/// <summary>
/// 事件带的一个 DV。
/// </summary>
public sealed class EventPayload
{
    [XmlAttribute("DVID")]
    public int Dvid { get; set; }
}

/// <summary>
/// EventDefinitions.xml 根节点。
/// </summary>
[XmlRoot("EventDefinitions")]
public sealed class EventDefinitionFile : IDefinitionFile<EventDefinition>
{
    [XmlElement("Event")]
    public List<EventDefinition> Items { get; set; } = new();
}
