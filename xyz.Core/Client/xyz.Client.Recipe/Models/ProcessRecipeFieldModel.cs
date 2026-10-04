using System.Globalization;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺步骤表的一列（后端字段表里的一个字段，来自 sc.xml）：列名、类型、范围、默认值、下拉的选项都按它来，界面不写死字段。
/// 一格怎么查也在这里，跟后端保存时的检查一样，提示文字用同一套错误码。
/// </summary>
public sealed class ProcessRecipeFieldModel
{
    /// <summary>
    /// 每种类型一列多宽（列宽不用配，按类型定）：数字框、下拉框、勾选框、文本框各一档。
    /// </summary>
    private const double NumberWidth = 118;

    private const double ChoiceWidth = 158;

    private const double BoolWidth = 96;

    private const double TextWidth = 200;

    /// <summary>
    /// 格子里的控件比列窄这么多，列与列之间留点空。
    /// </summary>
    private const double CellGap = 14;

    /// <summary>
    /// 英文界面的语言名开头（en-US 等）。
    /// </summary>
    private const string EnglishPrefix = "en";

    /// <summary>
    /// 开关的两个值（跟后端存的写法一样）。
    /// </summary>
    public const string TrueText = "true";

    public const string FalseText = "false";

    /// <summary>
    /// 整数、小数的写法跟通用输入框、后端一样：可以带正负号、小数点，不认千分位、科学计数法。
    /// </summary>
    private const NumberStyles IntegerStyle = NumberStyles.AllowLeadingSign;

    private const NumberStyles DecimalStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// 提示里上下限的写法（跟后端一样）。
    /// </summary>
    private const string LimitFormat = "0.##########";

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _optionsByParent;

    public ProcessRecipeFieldModel(ProcessRecipeFieldDto dto, string language)
    {
        bool english = language.StartsWith(EnglishPrefix, StringComparison.OrdinalIgnoreCase);
        Key = dto.Key;
        Text = english && !string.IsNullOrEmpty(dto.TextEn) ? dto.TextEn : dto.Text;
        Unit = dto.Unit ?? string.Empty;
        Type = dto.Type;
        Min = dto.Min;
        Max = dto.Max;
        Decimals = dto.Decimals;
        Default = dto.Default ?? string.Empty;
        Required = dto.Required;
        ParentKey = dto.ParentKey ?? string.Empty;
        Options = dto.Options ?? [];
        _optionsByParent = (dto.OptionsByParent ?? []).ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<string>)(pair.Value ?? []), StringComparer.OrdinalIgnoreCase);
        Header = Unit.Length == 0 ? Text : $"{Text} {Unit}";
        Width = Type switch
        {
            ProcessRecipeFieldType.Int => NumberWidth,
            ProcessRecipeFieldType.Double => NumberWidth,
            ProcessRecipeFieldType.Choice => ChoiceWidth,
            ProcessRecipeFieldType.Bool => BoolWidth,
            _ => TextWidth,
        };
        DataType = Type switch
        {
            ProcessRecipeFieldType.Int => InputDataType.Integer,
            ProcessRecipeFieldType.Double => InputDataType.Decimal,
            _ => InputDataType.Text,
        };
    }

    /// <summary>
    /// 字段名（存进配方、按它取值）。
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 列名（按界面语言取中文名或英文名）。
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// 表头：列名 + 单位。
    /// </summary>
    public string Header { get; }

    public string Unit { get; }

    public ProcessRecipeFieldType Type { get; }

    public double? Min { get; }

    public double? Max { get; }

    public int? Decimals { get; }

    /// <summary>
    /// 新加一步时填的值。
    /// </summary>
    public string Default { get; }

    public bool Required { get; }

    /// <summary>
    /// 选项跟着哪个字段走；空 = 不跟。
    /// </summary>
    public string ParentKey { get; }

    /// <summary>
    /// 不跟别的字段走时的选项。
    /// </summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>
    /// 这一列多宽（表头和每一格一样宽）。
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// 格子里的控件多宽。
    /// </summary>
    public double ControlWidth => Width - CellGap;

    /// <summary>
    /// 输入框按什么查格式（整数、小数、文本）。
    /// </summary>
    public InputDataType DataType { get; }

    /// <summary>
    /// 这一格是输入框（整数、小数、文本）。
    /// </summary>
    public bool IsInput => Type is ProcessRecipeFieldType.Int or ProcessRecipeFieldType.Double or ProcessRecipeFieldType.Text;

    public bool IsChoice => Type == ProcessRecipeFieldType.Choice;

    public bool IsBool => Type == ProcessRecipeFieldType.Bool;

    /// <summary>
    /// 这一步能选的：跟着别的字段走时按那个字段的值取，那个字段没选就什么都没有。
    /// </summary>
    public IReadOnlyList<string> OptionsFor(string parentValue)
    {
        if (ParentKey.Length == 0)
        {
            return Options;
        }

        return _optionsByParent.TryGetValue(parentValue.Trim(), out var list) ? list : [];
    }

    /// <summary>
    /// 查一格（跟后端保存时一样）：必填的不能空；整数、小数的写法、小数位、上下限；开关只认 true / false；下拉的值要在能选的里面。
    /// 没问题返回 null，有问题返回提示文字（步号、列名开头）。
    /// </summary>
    public string? Problem(string value, string number, IReadOnlyList<string> options)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return Required ? L10n.Get(ErrorCodes.ProcessRecipeValueRequired, number, Text) : null;
        }

        switch (Type)
        {
            case ProcessRecipeFieldType.Bool:
                bool isBool = string.Equals(trimmed, TrueText, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmed, FalseText, StringComparison.OrdinalIgnoreCase);
                return isBool ? null : L10n.Get(ErrorCodes.ProcessRecipeValueNotInOptions, number, Text, trimmed);
            case ProcessRecipeFieldType.Choice:
                return options.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
                    ? null
                    : L10n.Get(ErrorCodes.ProcessRecipeValueNotInOptions, number, Text, trimmed);
            case ProcessRecipeFieldType.Int:
                return long.TryParse(trimmed, IntegerStyle, CultureInfo.InvariantCulture, out long integer)
                    ? RangeProblem(integer, number)
                    : L10n.Get(ErrorCodes.ProcessRecipeValueNotInteger, number, Text, trimmed);
            case ProcessRecipeFieldType.Double:
                if (!decimal.TryParse(trimmed, DecimalStyle, CultureInfo.InvariantCulture, out decimal parsed))
                {
                    return L10n.Get(ErrorCodes.ProcessRecipeValueNotNumber, number, Text, trimmed);
                }

                int? decimals = Decimals;
                if (decimals is not null && DecimalPlaces(trimmed) > decimals.Value)
                {
                    return L10n.Get(ErrorCodes.ProcessRecipeValueTooPrecise, number, Text, decimals.Value.ToString(CultureInfo.InvariantCulture));
                }

                return RangeProblem((double)parsed, number);
            default:
                return null;
        }
    }

    private string? RangeProblem(double value, string number)
    {
        string unit = Unit.Length == 0 ? string.Empty : " " + Unit;
        double? min = Min;
        if (min is not null && value < min.Value)
        {
            return L10n.Get(ErrorCodes.ProcessRecipeValueBelowMin, number, Text, min.Value.ToString(LimitFormat, CultureInfo.InvariantCulture), unit);
        }

        double? max = Max;
        if (max is not null && value > max.Value)
        {
            return L10n.Get(ErrorCodes.ProcessRecipeValueAboveMax, number, Text, max.Value.ToString(LimitFormat, CultureInfo.InvariantCulture), unit);
        }

        return null;
    }

    /// <summary>
    /// 写了几位小数（末尾的 0 不算：1.50 算一位）。
    /// </summary>
    private static int DecimalPlaces(string value)
    {
        int dot = value.IndexOf('.');
        return dot < 0 ? 0 : value[(dot + 1)..].TrimEnd('0').Length;
    }
}
