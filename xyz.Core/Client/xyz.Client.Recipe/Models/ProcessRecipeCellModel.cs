using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺步骤一行里的一格：按这一列的类型显示输入框（整数、小数、文本）、下拉框或勾选框，值都按文字存。
/// 改了值告诉这一行（跟着它走的下拉要刷新选项，页面要标"有没保存的修改"、重新检查）。
/// </summary>
public sealed class ProcessRecipeCellModel : ObservableObject
{
    private readonly Action<ProcessRecipeCellModel> _changed;

    public ProcessRecipeCellModel(ProcessRecipeFieldModel field, string value, Action<ProcessRecipeCellModel> changed)
    {
        Field = field;
        _value = value;
        _changed = changed;
    }

    public ProcessRecipeFieldModel Field { get; }

    private string _value;

    /// <summary>
    /// 这一格的值：输入框提交后的文字、下拉选中的值（空 = 没选）、开关 true / false。
    /// 下拉框换选项时会先推一个 null 过来，不当真。
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

    private IReadOnlyList<ProcessRecipeChoiceModel> _choices = [];

    /// <summary>
    /// 下拉格的选项：第一项"—"（不选），后面是能选的。
    /// </summary>
    public IReadOnlyList<ProcessRecipeChoiceModel> Choices
    {
        get => _choices;
        private set => SetProperty(ref _choices, value);
    }

    /// <summary>
    /// 换下拉的选项（跟着别的字段走的，那个字段一变就换）。keepValue 为 false 时，原来选的不在新选项里就清掉；
    /// 换完再通知一次值，下拉框按值把选中项找回来（换选项时它把选中丢了）。
    /// </summary>
    public void RefreshChoices(IReadOnlyList<string> options, bool keepValue)
    {
        var choices = new List<ProcessRecipeChoiceModel> { new(string.Empty, L10n.Get("recipe.process.none")) };
        choices.AddRange(options.Select(option => new ProcessRecipeChoiceModel(option, option)));
        Choices = choices;
        if (!keepValue && _value.Length > 0 && !options.Contains(_value, StringComparer.OrdinalIgnoreCase))
        {
            Value = string.Empty;
            return;
        }

        OnPropertyChanged(nameof(Value));
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
