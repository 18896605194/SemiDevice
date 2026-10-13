using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 一格：工艺步骤一行里的一格，或配方头里能填的一项。按字段的类型显示输入框（整数、小数、文本）、下拉框或勾选框，值都按文字存。
/// 改了值告诉主人（页面要标"有没保存的修改"、重新检查）。
/// </summary>
public sealed class ProcessRecipeCellModel : ObservableObject
{
    private readonly Action<ProcessRecipeCellModel> _changed;

    /// <summary>
    /// options 是下拉能选的（步骤的按配方头选的 Type 取那一套）；不是下拉的不管。
    /// </summary>
    public ProcessRecipeCellModel(ProcessRecipeFieldModel field, string value, IReadOnlyList<string> options, Action<ProcessRecipeCellModel> changed)
    {
        Field = field;
        _value = value;
        _changed = changed;
        Choices = ChoicesOf(field, options);
    }

    public ProcessRecipeFieldModel Field { get; }

    private string _value;

    /// <summary>
    /// 这一格的值：输入框提交后的文字、下拉选中的值（空 = 没选）、开关 true / false。
    /// 下拉框重新绑数据时会先推一个 null 过来，不当真（没选是空字符串）。
    /// </summary>
    public string? Value
    {
        get => _value;
        set
        {
            if (value is null || value == _value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsChecked));
            _changed(this);
        }
    }

    /// <summary>
    /// 开关格的勾（绑勾选框）。
    /// </summary>
    public bool IsChecked
    {
        get => string.Equals(_value, ProcessRecipeFieldModel.TrueText, StringComparison.OrdinalIgnoreCase);
        set => Value = value ? ProcessRecipeFieldModel.TrueText : ProcessRecipeFieldModel.FalseText;
    }

    /// <summary>
    /// 下拉格的选项：第一项"—"（不选），后面是能选的；不是下拉的为空。
    /// </summary>
    public IReadOnlyList<ProcessRecipeChoiceModel> Choices { get; }

    private static IReadOnlyList<ProcessRecipeChoiceModel> ChoicesOf(ProcessRecipeFieldModel field, IReadOnlyList<string> options)
    {
        if (!field.IsChoice)
        {
            return [];
        }

        // 空字符串（比如腔体没配 Type 的那一种）就是"—"那一项，不再列一遍
        var choices = new List<ProcessRecipeChoiceModel> { new(string.Empty, L10n.Get("recipe.process.none")) };
        choices.AddRange(options.Where(option => option.Length > 0).Select(option => new ProcessRecipeChoiceModel(option, option)));
        return choices;
    }

    private bool _hasInputError;

    /// <summary>
    /// 输入框自己查出来的错（格式不对、超了范围，还没提交进来）：由输入框报回来。
    /// </summary>
    public bool HasInputError
    {
        get => _hasInputError;
        set
        {
            if (SetProperty(ref _hasInputError, value))
            {
                _changed(this);
            }
        }
    }

    private bool _hasError;

    /// <summary>
    /// 这一格检查没过：下拉框标红。由页面检查后填。
    /// </summary>
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }
}
