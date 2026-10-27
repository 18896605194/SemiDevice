using System.Globalization;
using System.Xml.Serialization;

namespace xyz.Components.Models;

/// <summary>
/// 一个工艺配方（存成文件的样子，一个编号一个文件）：配方头 + 步骤。配方头固定的几项是根节点的属性，
/// 字段表里自己加的项写在 Head 元素上（跟 Step 一样一项一个属性）。
/// 编号不进文件内容，由文件名定（001.xml 就是 1 号），免得文件名和内容对不上。
/// </summary>
[XmlRoot("ProcessRecipe")]
public class RecipeData
{
    /// <summary>
    /// 超时时间的写法跟字段值一样（可以带小数点，不认千分位、科学计数法）。
    /// </summary>
    private const NumberStyles TimeoutStyle = NumberStyles.AllowDecimalPoint;

    private const double MillisecondsPerSecond = 1000;

    /// <summary>
    /// 编号（1~Capacity），读文件时按文件名填。
    /// </summary>
    [XmlIgnore]
    public int Index { get; set; }

    [XmlAttribute]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 给哪种站点用（machine.xml 里站点配的 Type）：只在 Type 一样的腔体上跑，步骤下拉的选项也按它取。
    /// </summary>
    [XmlAttribute]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 超时时间（秒），按文字存（跟字段值一样，空 = 没填）；用的时候看 <see cref="TimeoutMilliseconds"/>。
    /// </summary>
    [XmlAttribute]
    public string Timeout { get; set; } = string.Empty;

    [XmlAttribute]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 配方头里自己在字段表加的项（字段名 → 值）。
    /// </summary>
    [XmlElement("Head")]
    public RecipeStep Head { get; set; } = new();

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
    public List<RecipeStep> Steps { get; set; } = [];

    /// <summary>
    /// 合计时长（各步时间加起来），秒；时间没填、填的不是数的那一步不算。
    /// </summary>
    [XmlIgnore]
    public double TotalSeconds => Steps.Sum(step => SecondsOf(step));

    /// <summary>
    /// 超时时间（秒）；没填、不是数的为 0。
    /// </summary>
    [XmlIgnore]
    public double TimeoutSeconds =>
        double.TryParse(Timeout, TimeoutStyle, CultureInfo.InvariantCulture, out double seconds) && seconds > 0 ? seconds : 0;

    /// <summary>
    /// 超时时间换成毫秒（腔体工艺动作的上限）；没填、不是数的为 0。
    /// </summary>
    [XmlIgnore]
    public int TimeoutMilliseconds => (int)Math.Min(int.MaxValue, Math.Round(TimeoutSeconds * MillisecondsPerSecond));

    /// <summary>
    /// 一步的时间（字段表里的 Seconds），秒；没填、不是数的算 0。
    /// </summary>
    public static double SecondsOf(RecipeStep step)
    {
        return double.TryParse(step.Get(RecipeField.SecondsKey), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out double seconds) && seconds > 0
            ? seconds
            : 0;
    }

    /// <summary>
    /// 深拷贝：给调用方的都是副本，拿不到库里的活对象。
    /// </summary>
    public RecipeData Clone()
    {
        return new RecipeData
        {
            Index = Index,
            Name = Name,
            Type = Type,
            Timeout = Timeout,
            Description = Description,
            Head = (Head ?? new RecipeStep()).Clone(),
            CreatedBy = CreatedBy,
            CreatedAt = CreatedAt,
            ModifiedBy = ModifiedBy,
            ModifiedAt = ModifiedAt,
            Revision = Revision,
            Steps = Steps.Select(step => step.Clone()).ToList(),
        };
    }
}
