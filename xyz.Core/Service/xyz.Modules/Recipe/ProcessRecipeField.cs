using System.Globalization;
using System.Text.RegularExpressions;
using xyz.Configs.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 工艺配方字段表里的一个字段（sc.xml ProcessRecipe → Fields 下一个子节点，节点名就是字段名），也就是步骤表的一列。
/// 类型决定界面用什么控件、怎么查：整数、小数按下限、上限、小数位查；下拉按数据源取选项；开关只认 true / false；文本只管必填。
/// 配错了（类型写错、上下限反了、默认值不合规、数据源写不对……）开机就抛，报清楚哪个节点哪个值。
/// </summary>
public sealed class ProcessRecipeField
{
    /// <summary>
    /// 步骤时长那个字段的名字：合计时长、腔体工艺超时的检查都靠它，字段表里必须有，类型是小数。
    /// </summary>
    public const string SecondsKey = "Seconds";

    /// <summary>
    /// 开关的两个值（存进文件就是这么写）。
    /// </summary>
    public const string TrueText = "true";

    public const string FalseText = "false";

    /// <summary>
    /// 字段名会写成配方文件里的属性名：字母开头，只用字母、数字、下划线。
    /// </summary>
    private static readonly Regex KeyRule = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// 整数、小数的写法跟界面的通用输入框一样：可以带正负号、小数点，不认千分位、科学计数法。
    /// </summary>
    private const NumberStyles IntegerStyle = NumberStyles.AllowLeadingSign;

    private const NumberStyles DecimalStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// 小数存进文件的写法：去掉末尾多余的 0（1.50 存成 1.5）。
    /// </summary>
    private const string DecimalFormat = "0.############################";

    /// <summary>
    /// 英文界面的语言名开头（en-US 等）。
    /// </summary>
    private const string EnglishPrefix = "en";

    public string Key { get; private init; } = string.Empty;

    public string Text { get; private init; } = string.Empty;

    /// <summary>
    /// 英文名；没配就用中文名。
    /// </summary>
    public string TextEn { get; private init; } = string.Empty;

    public ProcessRecipeFieldType Type { get; private init; }

    /// <summary>
    /// 单位（整数、小数才有）。
    /// </summary>
    public string Unit { get; private init; } = string.Empty;

    /// <summary>
    /// 下限；null = 不限。
    /// </summary>
    public double? Min { get; private init; }

    /// <summary>
    /// 上限；null = 不限。
    /// </summary>
    public double? Max { get; private init; }

    /// <summary>
    /// 最多几位小数（小数才有）；null = 不限。
    /// </summary>
    public int? Decimals { get; private init; }

    /// <summary>
    /// 新加一步时填的值；老配方里没有这个字段时也按它补。已经按字段规则查过。
    /// </summary>
    public string Default { get; private init; } = string.Empty;

    public bool Required { get; private init; }

    /// <summary>
    /// 数据源（下拉才有）。
    /// </summary>
    public ProcessRecipeSource? Source { get; private init; }

    public bool IsNumber => Type is ProcessRecipeFieldType.Int or ProcessRecipeFieldType.Double;

    /// <summary>
    /// 按界面语言取显示名：英文界面取英文名（没配就用中文名）。
    /// </summary>
    public string DisplayText(string? language)
    {
        bool english = language is not null && language.StartsWith(EnglishPrefix, StringComparison.OrdinalIgnoreCase);
        return english && TextEn.Length > 0 ? TextEn : Text;
    }

    /// <summary>
    /// 按字段自己的规则查一个非空的值：整数、小数的写法、上下限、小数位，开关只认 true / false。
    /// 下拉值在不在选项里要看数据源取出来的东西，由工艺配方库查。查过了返回 null；
    /// 没过返回错误码和参数（不含步号、字段名，调用方在前面补上）。
    /// </summary>
    public (string Code, string[] Args)? CheckFormat(string value)
    {
        if (Type == ProcessRecipeFieldType.Bool)
        {
            bool ok = string.Equals(value, TrueText, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, FalseText, StringComparison.OrdinalIgnoreCase);
            return ok ? null : (ErrorCodes.ProcessRecipeValueNotInOptions, [value]);
        }

        if (!IsNumber)
        {
            return null;
        }

        double number;
        if (Type == ProcessRecipeFieldType.Int)
        {
            if (!long.TryParse(value, IntegerStyle, CultureInfo.InvariantCulture, out long integer))
            {
                return (ErrorCodes.ProcessRecipeValueNotInteger, [value]);
            }

            number = integer;
        }
        else
        {
            if (!decimal.TryParse(value, DecimalStyle, CultureInfo.InvariantCulture, out decimal parsed))
            {
                return (ErrorCodes.ProcessRecipeValueNotNumber, [value]);
            }

            int decimals = Decimals ?? int.MaxValue;
            if (DecimalPlaces(value) > decimals)
            {
                return (ErrorCodes.ProcessRecipeValueTooPrecise, [IntText(decimals)]);
            }

            number = (double)parsed;
        }

        double? min = Min;
        if (min is not null && number < min.Value)
        {
            return (ErrorCodes.ProcessRecipeValueBelowMin, [Number(min.Value), UnitSuffix()]);
        }

        double? max = Max;
        if (max is not null && number > max.Value)
        {
            return (ErrorCodes.ProcessRecipeValueAboveMax, [Number(max.Value), UnitSuffix()]);
        }

        return null;
    }

    /// <summary>
    /// 存进文件的写法（查过之后调）：整数、小数去掉多余的写法（+5 存 5、1.50 存 1.5），开关小写，文本去掉首尾空白。
    /// 下拉的写法由工艺配方库按选项换（大小写跟数据源一样）。
    /// </summary>
    public string Canonical(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        switch (Type)
        {
            case ProcessRecipeFieldType.Int:
                return long.TryParse(trimmed, IntegerStyle, CultureInfo.InvariantCulture, out long integer)
                    ? integer.ToString(CultureInfo.InvariantCulture)
                    : trimmed;
            case ProcessRecipeFieldType.Double:
                return decimal.TryParse(trimmed, DecimalStyle, CultureInfo.InvariantCulture, out decimal number)
                    ? number.ToString(DecimalFormat, CultureInfo.InvariantCulture)
                    : trimmed;
            case ProcessRecipeFieldType.Bool:
                return trimmed.ToLowerInvariant();
            default:
                return trimmed;
        }
    }

    /// <summary>
    /// 从 sc.xml 的字段节点读一个字段；配错就抛，path 是节点路径（报错用）。
    /// </summary>
    public static ProcessRecipeField FromConfig(ModuleConfig node, string path)
    {
        string Read(string name)
        {
            var found = node.Values.FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
            return found is null ? string.Empty : (found.Value ?? string.Empty).Trim();
        }

        string key = node.Name.Trim();
        if (!KeyRule.IsMatch(key))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的名字 {key} 不能当字段名：字母开头，只用字母、数字、下划线");
        }

        string text = Read(nameof(Text));
        if (text.Length == 0)
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 没配 Text（配方页上这一列叫什么）");
        }

        string typeText = Read(nameof(Type));
        if (!Enum.TryParse(typeText, ignoreCase: true, out ProcessRecipeFieldType type) || !Enum.IsDefined(type) || int.TryParse(typeText, out _))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Type=\"{typeText}\" 不对：只能是 Int、Double、Choice、Bool、Text");
        }

        bool number = type is ProcessRecipeFieldType.Int or ProcessRecipeFieldType.Double;
        double? min = number ? ReadLimit(Read(nameof(Min)), nameof(Min), path, type) : null;
        double? max = number ? ReadLimit(Read(nameof(Max)), nameof(Max), path, type) : null;
        if (min is not null && max is not null && min.Value > max.Value)
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的下限 {Number(min.Value)} 比上限 {Number(max.Value)} 大");
        }

        int? decimals = null;
        string decimalsText = Read(nameof(Decimals));
        if (type == ProcessRecipeFieldType.Double && decimalsText.Length > 0)
        {
            if (!int.TryParse(decimalsText, NumberStyles.None, CultureInfo.InvariantCulture, out int places))
            {
                throw new InvalidOperationException($"sc.xml 节点 {path} 的 Decimals=\"{decimalsText}\" 要是 0 或正整数");
            }

            decimals = places;
        }

        bool required = false;
        string requiredText = Read(nameof(Required));
        if (requiredText.Length > 0 && !bool.TryParse(requiredText, out required))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Required=\"{requiredText}\" 只能是 true 或 false");
        }

        var source = type == ProcessRecipeFieldType.Choice ? ProcessRecipeSource.Parse(Read(nameof(Source)), path) : null;
        var field = new ProcessRecipeField
        {
            Key = key,
            Text = text,
            TextEn = Read(nameof(TextEn)),
            Type = type,
            Unit = number ? Read(nameof(Unit)) : string.Empty,
            Min = min,
            Max = max,
            Decimals = decimals,
            Required = required,
            Source = source,
        };

        string defaultText = Read(nameof(Default));
        if (defaultText.Length == 0)
        {
            return field;
        }

        // 默认值也得合规：新加一步就填它，不合规的话一添加就报错
        var problem = field.CheckFormat(defaultText);
        if (problem is not null)
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Default=\"{defaultText}\" 不合规（{problem.Value.Code}）");
        }

        if (source is not null && !source.IsParts && !source.Options.Contains(defaultText, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Default=\"{defaultText}\" 不在 Source 的选项里");
        }

        string canonical = source is not null && !source.IsParts
            ? source.Options.First(option => string.Equals(option, defaultText, StringComparison.OrdinalIgnoreCase))
            : field.Canonical(defaultText);
        return new ProcessRecipeField
        {
            Key = field.Key,
            Text = field.Text,
            TextEn = field.TextEn,
            Type = field.Type,
            Unit = field.Unit,
            Min = field.Min,
            Max = field.Max,
            Decimals = field.Decimals,
            Required = field.Required,
            Source = field.Source,
            Default = canonical,
        };
    }

    /// <summary>
    /// 读上限或下限：空 = 不限；整数字段的上下限也要是整数。
    /// </summary>
    private static double? ReadLimit(string text, string name, string path, ProcessRecipeFieldType type)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (!double.TryParse(text, DecimalStyle, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 {name}=\"{text}\" 不是数");
        }

        if (type == ProcessRecipeFieldType.Int && value != Math.Floor(value))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 是整数字段，{name}=\"{text}\" 也要是整数");
        }

        return value;
    }

    /// <summary>
    /// 写了几位小数（末尾的 0 不算：1.50 算一位）。
    /// </summary>
    private static int DecimalPlaces(string value)
    {
        int dot = value.IndexOf('.');
        return dot < 0 ? 0 : value[(dot + 1)..].TrimEnd('0').Length;
    }

    /// <summary>
    /// 提示里单位前面空一格；没单位就什么都不加（跟通用输入框的提示一样）。
    /// </summary>
    private string UnitSuffix()
    {
        return Unit.Length == 0 ? string.Empty : " " + Unit;
    }

    private static string IntText(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Number(double value)
    {
        return value.ToString("0.##########", CultureInfo.InvariantCulture);
    }
}
