using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 配方头的一项（后端配方头字段表里的一项，来自 machine.xml）：左边名字，右边值。程序算的（编号、名称、创建 / 修改记录、版本）只显示文字；
/// 能填的（Type、超时时间、说明、自己加的项）是一格，跟步骤的格子一样按类型显示输入框、下拉框或勾选框。
/// 一行排几项，文本项占三项宽（说明这类长的）。
/// </summary>
public sealed class ProcessRecipeHeadItemModel : ObservableObject
{
    /// <summary>
    /// 一项多宽（名字 + 值）；文本项三倍宽。
    /// </summary>
    private const double ItemWidth = 380;

    private const int WideSpan = 3;

    /// <summary>
    /// 左边名字占多宽，右边的值占剩下的（再留点空）。
    /// </summary>
    private const double LabelWidth = 100;

    private const double ItemGap = 20;

    /// <summary>
    /// 程序算的那几项。
    /// </summary>
    public ProcessRecipeHeadItemModel(ProcessRecipeFieldModel field, string autoText)
    {
        Field = field;
        _autoText = autoText;
        Width = ItemWidth;
    }

    /// <summary>
    /// 能填的那几项。
    /// </summary>
    public ProcessRecipeHeadItemModel(ProcessRecipeFieldModel field, ProcessRecipeCellModel cell)
    {
        Field = field;
        Cell = cell;
        _autoText = string.Empty;
        Width = field.IsInput && field.DataType == InputDataType.Text ? ItemWidth * WideSpan : ItemWidth;
    }

    public ProcessRecipeFieldModel Field { get; }

    /// <summary>
    /// 能填的那一格；程序算的为 null。
    /// </summary>
    public ProcessRecipeCellModel? Cell { get; }

    public bool IsAuto => Cell is null;

    public bool IsInput => Cell is not null && Field.IsInput;

    public bool IsChoice => Cell is not null && Field.IsChoice;

    public bool IsBool => Cell is not null && Field.IsBool;

    private string _autoText;

    /// <summary>
    /// 程序算的值（显示用的文字）。
    /// </summary>
    public string AutoText
    {
        get => _autoText;
        set => SetProperty(ref _autoText, value);
    }

    public double Width { get; }

    public double LabelColumnWidth => LabelWidth;

    /// <summary>
    /// 值的控件多宽。
    /// </summary>
    public double ControlWidth => Width - LabelWidth - ItemGap;
}
