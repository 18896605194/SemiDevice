using System.Globalization;
using System.Text.RegularExpressions;
using xyz.Components.Components;
using xyz.Configs.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;


public sealed class RecipeField
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
    /// sc.xml 里写下拉选项的参数名：选项用英文逗号隔开，如 <c>Time,Scan</c>。
    /// 步骤的下拉按站点的 Type 分开写：<c>Source.A</c> 是 Type 为 A 的站点那一套，<c>Source</c> 是站点没配 Type 的那一套。
    /// </summary>
    public const string SourceName = "Source";

    private const string SourceTypeSeparator = ".";

    /// <summary>
    /// 按晶圆尺寸分开写的上限：<c>Max.8</c>、<c>Max.12</c>（摆臂的位置这类晶圆坐标，8 寸中心 100、12 寸中心 150），
    /// 整机切到哪个尺寸用哪个；当前尺寸没写的用 <c>Max</c>。
    /// </summary>
    private const string MaxBySizePrefix = "Max.";

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
    /// sc.xml 里写的 <c>Max</c>：没按尺寸分开写时用它。上限按晶圆尺寸取，见 <see cref="MaxFor"/>。
    /// </summary>
    private double? DefaultMax { get; init; }

    /// <summary>
    /// 按晶圆尺寸分开写的上限（<c>Max.8</c>、<c>Max.12</c>）：尺寸 → 上限。
    /// </summary>
    private IReadOnlyDictionary<int, double> MaxBySize { get; init; } = new Dictionary<int, double>();

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
    /// 下拉的选项（下拉才有）：站点的 Type → 选项，照 sc.xml 里写的先后；键是空字符串的是 <c>Source</c>（站点没配 Type、配方头的下拉用它）。
    /// 摆臂、药液这类选腔体里设备的，写的就是 sc 里的设备名、药液名，腔体执行时照名字找设备（找不到这个腔体就跑不了这个配方）。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> OptionsByType { get; private init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public bool IsNumber => Type is ProcessRecipeFieldType.Int or ProcessRecipeFieldType.Double;

    /// <summary>
    /// 做这种尺寸的片时的上限（null = 不限）：写了 <c>Max.尺寸</c> 的用它，没写的用 <c>Max</c>。
    /// </summary>
    public double? MaxFor(int waferSize)
    {
        return MaxBySize.TryGetValue(waferSize, out double max) ? max : DefaultMax;
    }

    /// <summary>
    /// 这种站点能选的（Type 不分大小写）；没写这一套的为空。
    /// </summary>
    public IReadOnlyList<string> OptionsFor(string stationType)
    {
        return OptionsByType.TryGetValue(stationType.Trim(), out var options) ? options : [];
    }

    /// <summary>
    /// 按界面语言取显示名：英文界面取英文名（没配就用中文名）。
    /// </summary>
    public string DisplayText(string? language)
    {
        bool english = language is not null && language.StartsWith(EnglishPrefix, StringComparison.OrdinalIgnoreCase);
        return english && TextEn.Length > 0 ? TextEn : Text;
    }

    /// <summary>
    /// 按字段自己的规则查一个非空的值：整数、小数的写法、上下限、小数位，下拉要在 options 里（不分大小写；步骤按配方的 Type 取那一套），
    /// 开关只认 true / false。查过了返回 null；没过返回步骤那一组错误码和参数（不含步号、字段名，调用方在前面补上；配方头换成配方头那一组）。
    /// 上限按 waferSize（整机现在做几寸片）取。
    /// </summary>
    public (string Code, string[] Args)? CheckFormat(string value, IReadOnlyList<string> options, int waferSize)
    {
        double? max = MaxFor(waferSize);
        if (Type == ProcessRecipeFieldType.Choice)
        {
            return options.Contains(value, StringComparer.OrdinalIgnoreCase) ? null : (ErrorCodes.ProcessRecipeValueNotInOptions, [value]);
        }

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

        if (max is not null && number > max.Value)
        {
            return (ErrorCodes.ProcessRecipeValueAboveMax, [Number(max.Value), UnitSuffix()]);
        }

        return null;
    }

    /// <summary>
    /// 存进文件的写法（查过之后调）：整数、小数去掉多余的写法（+5 存 5、1.50 存 1.5），下拉换成 options 里的写法（大小写跟 sc.xml 一样），
    /// 开关小写，文本去掉首尾空白。
    /// </summary>
    public string Canonical(string value, IReadOnlyList<string> options)
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
            case ProcessRecipeFieldType.Choice:
                return options.FirstOrDefault(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
            case ProcessRecipeFieldType.Bool:
                return trimmed.ToLowerInvariant();
            default:
                return trimmed;
        }
    }

    /// <summary>
    /// 从 sc.xml 的字段节点读一个字段；配错就抛，path 是节点路径（报错用）。
    /// optionsInSc：下拉的选项写在 sc.xml 里（Source、Source.类型）；配方头的 Type 那一项传 false，它的选项是站点配的几种 Type。
    /// 下拉的默认值在不在选项里，要知道有哪几种站点才查得了，由工艺配方库 Bind 时查。
    /// </summary>
    public static RecipeField FromConfig(ModuleConfig node, string path, bool optionsInSc = true)
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
        double? max = number ? ReadLimit(Read("Max"), "Max", path, type) : null;
        var maxBySize = number ? ReadMaxBySize(node, path, type) : new Dictionary<int, double>();
        foreach (double limit in maxBySize.Values.Append(max ?? double.PositiveInfinity))
        {
            if (min is not null && min.Value > limit)
            {
                throw new InvalidOperationException($"sc.xml 节点 {path} 的下限 {Number(min.Value)} 比上限 {Number(limit)} 大");
            }
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

        bool choice = type == ProcessRecipeFieldType.Choice;
        var optionsByType = choice && optionsInSc
            ? ReadOptions(node, path)
            : new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var field = new RecipeField
        {
            Key = key,
            Text = text,
            TextEn = Read(nameof(TextEn)),
            Type = type,
            Unit = number ? Read(nameof(Unit)) : string.Empty,
            Min = min,
            DefaultMax = max,
            MaxBySize = maxBySize,
            Decimals = decimals,
            Required = required,
            OptionsByType = optionsByType,
        };

        string defaultText = Read(nameof(Default));
        if (defaultText.Length == 0)
        {
            return field;
        }

        // 默认值也得合规：新加一步就填它，不合规的话一添加就报错。下拉的先原样留着，Bind 时按每种站点的选项查。
        // 上限按尺寸分开写的，每种尺寸都查一遍（切了尺寸照样能添加）
        if (!choice)
        {
            foreach (int size in new[] { WaferSizeComponent.Inch8, WaferSizeComponent.Inch12 })
            {
                var problem = field.CheckFormat(defaultText, [], size);
                if (problem is not null)
                {
                    throw new InvalidOperationException($"sc.xml 节点 {path} 的 Default=\"{defaultText}\" 不合规（{problem.Value.Code}）");
                }
            }
        }

        return new RecipeField
        {
            Key = field.Key,
            Text = field.Text,
            TextEn = field.TextEn,
            Type = field.Type,
            Unit = field.Unit,
            Min = field.Min,
            DefaultMax = field.DefaultMax,
            MaxBySize = field.MaxBySize,
            Decimals = field.Decimals,
            Required = field.Required,
            OptionsByType = field.OptionsByType,
            Default = choice ? defaultText : field.Canonical(defaultText, []),
        };
    }

    /// <summary>
    /// 读下拉的选项：<c>Source</c>（站点没配 Type 的那一套）和 <c>Source.类型</c>（按站点的 Type 分开写的），每一套英文逗号隔开、
    /// 去掉首尾空白、按写的先后。一套都没写、某一套是空的、选项写了两遍、同一种 Type 写了两遍（不分大小写）就抛。
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> ReadOptions(ModuleConfig node, string path)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in node.Values)
        {
            string name = (value.Name ?? string.Empty).Trim();
            string stationType;
            if (string.Equals(name, SourceName, StringComparison.OrdinalIgnoreCase))
            {
                stationType = string.Empty;
            }
            else if (name.StartsWith(SourceName + SourceTypeSeparator, StringComparison.OrdinalIgnoreCase))
            {
                stationType = name[(SourceName.Length + SourceTypeSeparator.Length)..].Trim();
            }
            else
            {
                continue;
            }

            string text = (value.Value ?? string.Empty).Trim();
            var options = new List<string>();
            foreach (string option in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (options.Contains(option, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"sc.xml 节点 {path} 的 {name}=\"{text}\" 里选项 {option} 写了两遍");
                }

                options.Add(option);
            }

            if (options.Count == 0)
            {
                throw new InvalidOperationException($"sc.xml 节点 {path} 的 {name} 一个选项都没有（英文逗号隔开，如 Time,Scan）");
            }

            if (!result.TryAdd(stationType, options))
            {
                throw new InvalidOperationException($"sc.xml 节点 {path} 的 {name} 写了两遍（Type 不分大小写）");
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException(
                $"sc.xml 节点 {path} 是下拉，要写选项：{SourceName}（站点没配 Type 时），或按站点的 Type 分开写 {SourceName}{SourceTypeSeparator}A、{SourceName}{SourceTypeSeparator}B……");
        }

        return result;
    }

    /// <summary>
    /// 读按晶圆尺寸分开写的上限（<c>Max.8</c>、<c>Max.12</c>）：尺寸只能是 8 或 12、值要是数（空着的跳过），同一个尺寸写了两遍就抛。
    /// </summary>
    private static Dictionary<int, double> ReadMaxBySize(ModuleConfig node, string path, ProcessRecipeFieldType type)
    {
        var result = new Dictionary<int, double>();
        foreach (var value in node.Values)
        {
            string name = (value.Name ?? string.Empty).Trim();
            if (!name.StartsWith(MaxBySizePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string sizeText = name[MaxBySizePrefix.Length..].Trim();
            if (!int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out int size) || !WaferSizeComponent.IsSupported(size))
            {
                throw new InvalidOperationException(
                    $"sc.xml 节点 {path} 的 {name} 不对：按晶圆尺寸分开写的上限只能是 {MaxBySizePrefix}{WaferSizeComponent.Inch8} 或 {MaxBySizePrefix}{WaferSizeComponent.Inch12}");
            }

            double? limit = ReadLimit((value.Value ?? string.Empty).Trim(), name, path, type);
            if (limit is null)
            {
                continue;
            }

            if (!result.TryAdd(size, limit.Value))
            {
                throw new InvalidOperationException($"sc.xml 节点 {path} 的 {name} 写了两遍");
            }
        }

        return result;
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
