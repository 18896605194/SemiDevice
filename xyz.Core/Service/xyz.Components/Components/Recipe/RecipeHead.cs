using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// 配方头固定的几项（machine.xml ProcessRecipe → Head 字段表里必须有，字段名、类型不能改，中文名、英文名、先后能改）：
/// 编号、名称、创建人、创建时间、修改人、修改时间、版本由程序算，页面上只显示；Type、超时时间、说明在页面上填。
/// 字段表里别的项是自己加的，都在页面上填，值存在配方的 Head 里。
/// </summary>
public static class RecipeHead
{
    public const string IndexKey = "Index";

    public const string NameKey = "Name";

    /// <summary>
    /// 配方给哪种站点用：选项是 machine.xml 里站点配的几种 Type（BaseModule.Type）。
    /// </summary>
    public const string TypeKey = "Type";

    /// <summary>
    /// 超时时间（秒）：腔体跑这份配方的上限，合计时长不能超过它。
    /// </summary>
    public const string TimeoutKey = "Timeout";

    public const string DescriptionKey = "Description";

    public const string CreatedByKey = "CreatedBy";

    public const string CreatedAtKey = "CreatedAt";

    public const string ModifiedByKey = "ModifiedBy";

    public const string ModifiedAtKey = "ModifiedAt";

    public const string RevisionKey = "Revision";

    /// <summary>
    /// 固定的几项：字段名 → 类型（字段表里配的类型要跟它一样）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ProcessRecipeFieldType> Fixed =
        new Dictionary<string, ProcessRecipeFieldType>(StringComparer.OrdinalIgnoreCase)
        {
            [IndexKey] = ProcessRecipeFieldType.Int,
            [NameKey] = ProcessRecipeFieldType.Text,
            [TypeKey] = ProcessRecipeFieldType.Choice,
            [TimeoutKey] = ProcessRecipeFieldType.Double,
            [DescriptionKey] = ProcessRecipeFieldType.Text,
            [CreatedByKey] = ProcessRecipeFieldType.Text,
            [CreatedAtKey] = ProcessRecipeFieldType.Text,
            [ModifiedByKey] = ProcessRecipeFieldType.Text,
            [ModifiedAtKey] = ProcessRecipeFieldType.Text,
            [RevisionKey] = ProcessRecipeFieldType.Int,
        };

    /// <summary>
    /// 程序算的那几项（固定项里除了 Type、超时时间、说明）：页面上只显示，保存时不收。
    /// </summary>
    public static bool IsAuto(string key)
    {
        return Fixed.ContainsKey(key)
            && !string.Equals(key, TypeKey, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(key, TimeoutKey, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(key, DescriptionKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 自己在字段表里加的项（不是固定的那几项）：值存在配方的 Head 里。
    /// </summary>
    public static bool IsCustom(string key)
    {
        return !Fixed.ContainsKey(key);
    }
}
