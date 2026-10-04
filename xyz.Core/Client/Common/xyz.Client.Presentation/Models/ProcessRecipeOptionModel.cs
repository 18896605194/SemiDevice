using System.Globalization;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 选工艺配方弹窗里的一行（流程配方页、腔体手动页都用）：编号、名称、说明、时长。
/// 选中后带回的是名称——流程配方、腔体起工艺都按名字引用工艺配方。
/// </summary>
public sealed class ProcessRecipeOptionModel
{
    /// <summary>
    /// 编号至少显示两位（01）；个数上百、上千时跟着变宽。
    /// </summary>
    private const int MinIndexDigits = 2;

    /// <summary>
    /// 时长的写法：最多一位小数。
    /// </summary>
    private const string SecondsFormat = "0.#";

    private ProcessRecipeOptionModel(string no, string name, string description, string durationText)
    {
        No = no;
        Name = name;
        Description = description;
        DurationText = durationText;
    }

    public string No { get; }

    public string Name { get; }

    public string Description { get; }

    /// <summary>
    /// 合计时长（如"115 s"）。
    /// </summary>
    public string DurationText { get; }

    /// <summary>
    /// 按后端的工艺配方列表建一组，编号位数跟着个数走。
    /// </summary>
    public static List<ProcessRecipeOptionModel> From(ProcessRecipeListDto list)
    {
        int digits = Math.Max(MinIndexDigits, list.Capacity.ToString(CultureInfo.InvariantCulture).Length);
        string indexFormat = "D" + digits.ToString(CultureInfo.InvariantCulture);
        return list.Items.Select(item => new ProcessRecipeOptionModel(
            item.Index.ToString(indexFormat, CultureInfo.InvariantCulture),
            item.Name,
            item.Description,
            L10n.Get("recipe.process.seconds", item.TotalSeconds.ToString(SecondsFormat, CultureInfo.InvariantCulture)))).ToList();
    }
}
