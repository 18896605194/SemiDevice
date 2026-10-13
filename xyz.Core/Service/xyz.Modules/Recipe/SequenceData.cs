using System.Xml.Serialization;

namespace xyz.Modules;


[XmlRoot("Sequence")]
public class SequenceData
{
    /// <summary>
    /// 编号（1~Capacity），读文件时按文件名填。
    /// </summary>
    [XmlIgnore]
    public int Index { get; set; }

    [XmlAttribute]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute]
    public string Description { get; set; } = string.Empty;

    [XmlAttribute]
    public string CreatedBy { get; set; } = string.Empty;

    [XmlAttribute]
    public DateTime CreatedAt { get; set; }

    [XmlAttribute]
    public string ModifiedBy { get; set; } = string.Empty;

    [XmlAttribute]
    public DateTime ModifiedAt { get; set; }

    [XmlAttribute]
    public int Revision { get; set; }

    /// <summary>
    /// 步骤：第 1 步、最后一步是 LoadPort 分组（取片、放片），中间是片要经过的站点。
    /// </summary>
    [XmlElement("Step")]
    public List<SequenceStep> Steps { get; set; } = [];

    /// <summary>
    /// 深拷贝
    /// </summary>
    public SequenceData Clone()
    {
        return new SequenceData
        {
            Index = Index,
            Name = Name,
            Description = Description,
            CreatedBy = CreatedBy,
            CreatedAt = CreatedAt,
            ModifiedBy = ModifiedBy,
            ModifiedAt = ModifiedAt,
            Revision = Revision,
            Steps = Steps.Select(step => step.Clone()).ToList(),
        };
    }
}
