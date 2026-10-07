using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Serialization;

namespace xyz.Modules;

/// <summary>
/// 工艺配方的一步（存成文件的样子）：每个字段一个值，字段名 → 值，都按文字存；有哪些字段由 sc.xml 的字段表定。
/// 文件里一个字段写成 Step 的一个属性（如 <c>&lt;Step Seconds="5" Rpm="300" Arm="Arm1" … /&gt;</c>），
/// 存的时候字段表里的字段都写上（空的也写），这样字段表以后加了字段，老配方里看得出"这个字段当时还没有"，打开时按默认值补。
/// </summary>
public class ProcessRecipeStep
{
    /// <summary>
    /// 这一步的值，按字段表的先后。
    /// </summary>
    [XmlIgnore]
    public List<ProcessRecipeValue> Values { get; set; } = [];

    /// <summary>
    /// 给 XmlSerializer 用的：每个值写成一个属性，读的时候所有属性都收进来。转 JSON（Host 远程取、下配方）时不要它，值在 <see cref="Values"/> 里。
    /// </summary>
    [XmlAnyAttribute]
    [JsonIgnore]
    public XmlAttribute[] Attributes
    {
        get
        {
            var document = new XmlDocument();
            return Values.Select(item =>
            {
                var attribute = document.CreateAttribute(item.Name);
                attribute.Value = item.Value;
                return attribute;
            }).ToArray();
        }
        set
        {
            Values = (value ?? []).Select(attribute => new ProcessRecipeValue(attribute.Name, attribute.Value)).ToList();
        }
    }

    /// <summary>
    /// 取一个字段的值（字段名不分大小写）；没有就是空的。
    /// </summary>
    public string Get(string key)
    {
        var found = Values.FirstOrDefault(item => string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase));
        return found is null ? string.Empty : found.Value;
    }

    /// <summary>
    /// 有没有这个字段（空值也算有）：老配方里没有的字段打开时按默认值补。
    /// </summary>
    public bool Has(string key)
    {
        return Values.Any(item => string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    public ProcessRecipeStep Clone()
    {
        return new ProcessRecipeStep { Values = [.. Values] };
    }
}

/// <summary>
/// 一步里一个字段的值。
/// </summary>
public sealed record ProcessRecipeValue(string Name, string Value);
