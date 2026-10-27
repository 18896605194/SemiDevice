using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Serialization;

namespace xyz.Components.Models;


public class RecipeStep
{
    /// <summary>
    /// 这一步的值，按字段表的先后。
    /// </summary>
    [XmlIgnore]
    public List<RecipeValue> Values { get; set; } = [];

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
            Values = (value ?? []).Select(attribute => new RecipeValue(attribute.Name, attribute.Value)).ToList();
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

    public RecipeStep Clone()
    {
        return new RecipeStep { Values = [.. Values] };
    }
}
