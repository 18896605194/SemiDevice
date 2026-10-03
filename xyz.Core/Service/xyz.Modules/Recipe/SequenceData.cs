using System.Xml.Serialization;

namespace xyz.Modules;

/// <summary>
/// 一个流程配方（存成文件的样子，一个编号一个文件）：头信息 + 步骤。
/// 编号不进文件内容，由文件名定（001.xml 就是 1 号），免得文件名和内容对不上。
/// </summary>
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

    /// <summary>
    /// 版本：新建是 1，每次保存、改名加 1。保存时对一下，对不上说明别处改过。
    /// </summary>
    [XmlAttribute]
    public int Revision { get; set; }

    /// <summary>
    /// 步骤：第 1 步、最后一步是 LoadPort 分组（取片、放片），中间是片要经过的站点。
    /// </summary>
    [XmlElement("Step")]
    public List<SequenceStep> Steps { get; set; } = [];

    /// <summary>
    /// 深拷贝：给调用方的都是副本，拿不到库里的活对象。
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
