using System.Globalization;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺步骤表的一列、配方头的一项（后端字段表里的一个字段，来自 sc.xml）：名字、类型、范围、默认值、下拉的选项都按它来，界面不写死字段。
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

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _optionsByType;

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
        IsAuto = dto.IsAuto;
        Options = dto.Options ?? [];
        _optionsByType = (dto.OptionsByType ?? []).ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<string>)(pair.Value ?? []), StringComparer.OrdinalIgnoreCase);
        Header = Unit.Length == 0 ? Text : $"{Text} {Unit}";
        switch (Type)
        {
            case ProcessRecipeFieldType.Int:
                Width = NumberWidth;
                break;
            case ProcessRecipeFieldType.Double:
                Width = NumberWidth;
                break;
            case ProcessRecipeFieldType.Choice:
                Width = ChoiceWidth;
                break;
            case ProcessRecipeFieldType.Bool:
                Width = BoolWidth;
                break;
            default:
                Width = TextWidth;
                break;
        }

        switch (Type)
        {
            case ProcessRecipeFieldType.Int:
                DataType = InputDataType.Integer;
                break;
            case ProcessRecipeFieldType.Double:
                DataType = InputDataType.Decimal;
                break;
            default:
                DataType = InputDataType.Text;
                break;
        }
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
    /// 配方头里程序算的那几项：只显示、不能改。
    /// </summary>
    public bool IsAuto { get; }

    /// <summary>
    /// 配方头下拉的选项（Type 那一项是腔体配的几种 Type）。
    /// </summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>
    /// 下拉能选的：步骤的按配方头选的 Type 取那一套（sc.xml 里按站点 Type 分开写的）；配方头的就是 <see cref="Options"/>。
    /// </summary>
    public IReadOnlyList<string> OptionsFor(string stationType)
    {
        if (_optionsByType.Count == 0)
        {
            return Options;
        }

        return _optionsByType.TryGetValue(stationType.Trim(), out var options) ? options : [];
    }

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
    /// 查一格（跟后端保存时一样）：必填的不能空；整数、小数的写法、小数位、上下限；开关只认 true / false；下拉的值要在 options 里。
    /// stepNumber 是步骤格子的步号；配方头的项传 null，提示用配方头那一组。没问题返回 null，有问题返回提示文字（步号、名字开头）。
    /// </summary>
    public string? Problem(string value, string? stepNumber, IReadOnlyList<string> options)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return Required ? Message(stepNumber, ErrorCodes.ProcessRecipeValueRequired, ErrorCodes.ProcessRecipeHeadValueRequired) : null;
        }

        switch (Type)
        {
            case ProcessRecipeFieldType.Bool:
                bool isBool = string.Equals(trimmed, TrueText, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmed, FalseText, StringComparison.OrdinalIgnoreCase);
                return isBool
                    ? null
                    : Message(stepNumber, ErrorCodes.ProcessRecipeValueNotInOptions, ErrorCodes.ProcessRecipeHeadValueNotInOptions, trimmed);
            case ProcessRecipeFieldType.Choice:
                return options.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
                    ? null
                    : Message(stepNumber, ErrorCodes.ProcessRecipeValueNotInOptions, ErrorCodes.ProcessRecipeHeadValueNotInOptions, trimmed);
            case ProcessRecipeFieldType.Int:
                return long.TryParse(trimmed, IntegerStyle, CultureInfo.InvariantCulture, out long integer)
                    ? RangeProblem(integer, stepNumber)
                    : Message(stepNumber, ErrorCodes.ProcessRecipeValueNotInteger, ErrorCodes.ProcessRecipeHeadValueNotInteger, trimmed);
            case ProcessRecipeFieldType.Double:
                if (!decimal.TryParse(trimmed, DecimalStyle, CultureInfo.InvariantCulture, out decimal parsed))
                {
                    return Message(stepNumber, ErrorCodes.ProcessRecipeValueNotNumber, ErrorCodes.ProcessRecipeHeadValueNotNumber, trimmed);
                }

                int? decimals = Decimals;
                if (decimals is not null && DecimalPlaces(trimmed) > decimals.Value)
                {
                    return Message(stepNumber, ErrorCodes.ProcessRecipeValueTooPrecise, ErrorCodes.ProcessRecipeHeadValueTooPrecise,
                        decimals.Value.ToString(CultureInfo.InvariantCulture));
                }

                return RangeProblem((double)parsed, stepNumber);
            default:
                return null;
        }
    }

    private string? RangeProblem(double value, string? stepNumber)
    {
        string unit = Unit.Length == 0 ? string.Empty : " " + Unit;
        double? min = Min;
        if (min is not null && value < min.Value)
        {
            return Message(stepNumber, ErrorCodes.ProcessRecipeValueBelowMin, ErrorCodes.ProcessRecipeHeadValueBelowMin,
                min.Value.ToString(LimitFormat, CultureInfo.InvariantCulture), unit);
        }

        double? max = Max;
        if (max is not null && value > max.Value)
        {
            return Message(stepNumber, ErrorCodes.ProcessRecipeValueAboveMax, ErrorCodes.ProcessRecipeHeadValueAboveMax,
                max.Value.ToString(LimitFormat, CultureInfo.InvariantCulture), unit);
        }

        return null;
    }

    /// <summary>
    /// 按语言包出提示：步骤的格子用步骤那一组（步号、名字开头），配方头的项（stepNumber 为 null）用配方头那一组（名字开头）。
    /// </summary>
    private string Message(string? stepNumber, string stepCode, string headCode, params string[] args)
    {
        return stepNumber is null
            ? L10n.Get(headCode, [Text, .. args])
            : L10n.Get(stepCode, [stepNumber, Text, .. args]);
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
