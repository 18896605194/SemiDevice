using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺步骤的一行：一个字段一格，按字段表的先后（字段表来自 sc.xml，界面不写死字段）。
/// 一格改了，跟着它走的下拉（数据源写了 @这个字段，比如药液跟着摆臂）换选项，原来选的不在新选项里就清掉；再告诉页面。
/// </summary>
public sealed class ProcessRecipeStepModel : ObservableObject
{
    private readonly Action<ProcessRecipeStepModel> _changed;

    public ProcessRecipeStepModel(IReadOnlyList<ProcessRecipeFieldModel> fields, IReadOnlyDictionary<string, string>? values,
        Action<ProcessRecipeStepModel> changed)
    {
        _changed = changed;
        Cells = fields.Select(field => new ProcessRecipeCellModel(field, ValueOf(values, field.Key), OnCellChanged)).ToList();
        foreach (var cell in Cells.Where(item => item.Field.IsChoice))
        {
            cell.RefreshChoices(cell.Field.OptionsFor(Get(cell.Field.ParentKey)), keepValue: true);
        }
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

    private void OnCellChanged(ProcessRecipeCellModel cell)
    {
        foreach (var dependent in Cells.Where(item => item.Field.IsChoice && string.Equals(item.Field.ParentKey, cell.Field.Key, StringComparison.OrdinalIgnoreCase)))
        {
            dependent.RefreshChoices(dependent.Field.OptionsFor(cell.Value ?? string.Empty), keepValue: false);
        }

        _changed(this);
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
