using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺步骤的一行：一个字段一格，按字段表的先后（字段表来自 sc.xml，界面不写死字段）。一格改了就告诉页面；字段之间不互相管。
/// 下拉的选项按配方头选的 Type 取那一套（Type 换了页面整行重摆）。
/// </summary>
public sealed class ProcessRecipeStepModel : ObservableObject
{
    public ProcessRecipeStepModel(IReadOnlyList<ProcessRecipeFieldModel> fields, IReadOnlyDictionary<string, string>? values, string stationType,
        Action<ProcessRecipeStepModel> changed)
    {
        Cells = fields.Select(field => new ProcessRecipeCellModel(field, ValueOf(values, field.Key), field.OptionsFor(stationType), _ => changed(this)))
            .ToList();
    }

    /// <summary>
    /// 这一行的格子，按字段表的先后。
    /// </summary>
    public IReadOnlyList<ProcessRecipeCellModel> Cells { get; }

    private int _number;

    /// <summary>
    /// 步号（从 1 起，跟后端提示里的步号一样）。
    /// </summary>
    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    private bool _hasError;

    /// <summary>
    /// 这一行有问题：整行淡红底。由页面检查后填。
    /// </summary>
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    /// <summary>
    /// 取一格的值（字段名不分大小写）；没有这个字段就是空的。
    /// </summary>
    public string Get(string key)
    {
        var cell = Cells.FirstOrDefault(item => string.Equals(item.Field.Key, key, StringComparison.OrdinalIgnoreCase));
        return cell is null ? string.Empty : cell.Value ?? string.Empty;
    }

    /// <summary>
    /// 转成保存请求里的一步：每个字段一个值（去掉首尾空白）。
    /// </summary>
    public ProcessRecipeStepDto ToDto()
    {
        var dto = new ProcessRecipeStepDto();
        foreach (var cell in Cells)
        {
            dto.Values[cell.Field.Key] = (cell.Value ?? string.Empty).Trim();
        }

        return dto;
    }

    private static string ValueOf(IReadOnlyDictionary<string, string>? values, string key)
    {
        if (values is null)
        {
            return string.Empty;
        }

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
