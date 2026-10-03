using System.Xml.Serialization;

namespace xyz.Modules;

/// <summary>
/// 流程配方的一步：站点分组（sc.xml 里的分组节点名，如 LoadPort、Chamber）、这一步可去的站点（勾几个 = 哪个空去哪个）、
/// 工艺配方（分组要的时候才有，比如工艺腔）。
/// </summary>
public class SequenceStep
{
    /// <summary>
    /// 给 XML 读文件用。
    /// </summary>
    public SequenceStep()
    {
    }

    public SequenceStep(string group, IEnumerable<string> stations, string recipe = "")
    {
        Group = group;
        Stations = stations.ToList();
        Recipe = recipe;
    }

    [XmlAttribute]
    public string Group { get; set; } = string.Empty;

    [XmlAttribute]
    public string Recipe { get; set; } = string.Empty;

    [XmlElement("Station")]
    public List<string> Stations { get; set; } = [];

    public SequenceStep Clone()
    {
        return new SequenceStep(Group, Stations, Recipe);
    }
}
