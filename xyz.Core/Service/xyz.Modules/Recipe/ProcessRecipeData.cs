using System.Xml.Serialization;

namespace xyz.Modules;

/// <summary>
/// 一个工艺配方（存成文件的样子，一个编号一个文件）：头信息 + 步骤。
/// 编号不进文件内容，由文件名定（001.xml 就是 1 号），免得文件名和内容对不上。
/// </summary>
[XmlRoot("ProcessRecipe")]
public class ProcessRecipeData
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

    [XmlElement("Step")]
    public List<ProcessRecipeStep> Steps { get; set; } = [];

    /// <summary>
    /// 合计时长（各步时间加起来），秒。
    /// </summary>
    [XmlIgnore]
    public double TotalSeconds => Steps.Sum(step => step.Seconds);

    /// <summary>
    /// 深拷贝：给调用方的都是副本，拿不到库里的活对象。
    /// </summary>
    public ProcessRecipeData Clone()
    {
        return new ProcessRecipeData
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
