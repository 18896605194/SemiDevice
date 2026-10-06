using System.Collections;
using System.Globalization;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// 设备侧的值落成 SECS-II 数据项（SV、EC、DV 都走这里），和 Host 给的值转回设备侧的写法。
/// 按值本身的类型落格式：byte → U1、ushort → U2、uint → U4、bool → Boolean、double → F8、列表 → L……；
/// 直接给 SecsItem 的原样用（GEM300 里格式写死的那些，比如状态值 U1、槽图 L[n] U1）。
/// int / long 沿用老规矩：非负给 U4（放不下给 U8），负数给 I4 / I8。
/// </summary>
internal static class GemValue
{
    /// <summary>E5 规定 A 类型只能是 ASCII。</summary>
    private const char MaxAscii = '\x7F';

    /// <summary>
    /// 值 → 数据项。format 是编号表里声明的格式（Int / Double / Bool / String / Enum）：值是字符串时按它解析，
    /// 是枚举时 Enum 格式报名字（沿用 S1F3 的老规矩）、别的格式报数值。null 给空项（按格式的零长度项）。
    /// </summary>
    public static SecsItem From(object? value, string format)
    {
        switch (value)
        {
            case null:
                return Empty(format);

            case SecsItem item:
                return item;

            case bool flag:
                return SecsItem.Boolean(flag);

            case byte number:
                return SecsItem.U1(number);

            case sbyte number:
                return SecsItem.I1(number);

            case short number:
                return SecsItem.I2(number);

            case ushort number:
                return SecsItem.U2(number);

            case uint number:
                return SecsItem.U4(number);

            case ulong number:
                return SecsItem.U8(number);

            case int number:
                return number >= 0 ? SecsItem.U4((uint)number) : SecsItem.I4(number);

            case long number:
                return Integer(number);

            case float real:
                return SecsItem.F4(real);

            case double real:
                return SecsItem.F8(real);

            case decimal real:
                return SecsItem.F8((double)real);

            case DateTime time:
                return SecsItem.A(Time(time, 1));

            case Enum member:
                return string.Equals(format, "Enum", StringComparison.OrdinalIgnoreCase)
                    ? SecsItem.A(Ascii(member.ToString()))
                    : Integer(Convert.ToInt64(member, CultureInfo.InvariantCulture));

            case string text:
                return FromText(text, format);

            case IEnumerable items:
                return SecsItem.L(items.Cast<object?>().Select(element => From(element, string.Empty)));

            default:
                return SecsItem.A(Ascii(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
        }
    }

    /// <summary>
    /// 字符串值按声明的格式解析（EC 的值、老的字符串 SV）：Bool → Boolean、Int → 整数、Double → F8，别的给 A。
    /// 解析不了的退回 A 原样发，别让一条脏值毁掉整条回复。空字符串发一个空格（有的 Host 收零长度 A 会出错）。
    /// </summary>
    public static SecsItem FromText(string text, string format)
    {
        switch (format)
        {
            case "Bool":
                if (bool.TryParse(text, out bool flag))
                {
                    return SecsItem.Boolean(flag);
                }

                break;

            case "Int":
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
                {
                    return Integer(number);
                }

                break;

            case "Double":
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
                {
                    return SecsItem.F8(real);
                }

                break;
        }

        return SecsItem.A(string.IsNullOrEmpty(text) ? " " : Ascii(text));
    }

    /// <summary>没有值的时候报的空项：按格式给零长度的数组（Host 按格式认得出"这一项这次没值"）。</summary>
    public static SecsItem Empty(string format)
    {
        return format switch
        {
            "Bool" => SecsItem.Boolean(),
            "Int" => SecsItem.U4(),
            "Double" => SecsItem.F8(),
            _ => SecsItem.A(string.Empty),
        };
    }

    /// <summary>整数：非负给 U4（放不下给 U8），负数给 I4（放不下给 I8）。</summary>
    public static SecsItem Integer(long number)
    {
        if (number >= 0)
        {
            return number <= uint.MaxValue ? SecsItem.U4((uint)number) : SecsItem.U8((ulong)number);
        }

        return number >= int.MinValue ? SecsItem.I4((int)number) : SecsItem.I8(number);
    }

    /// <summary>A 类型只能是 ASCII：中文这类字符换成 ?——编码器碰到非 ASCII 会直接报错，一个字符就能毁掉整条报文。</summary>
    public static string Ascii(string text)
    {
        return string.Concat(text.Select(ch => ch <= MaxAscii ? ch : '?'));
    }

    /// <summary>
    /// 时间按 E30 的 TimeFormat：0 = 12 位 YYMMDDhhmmss，1 = 16 位 YYYYMMDDhhmmsscc（带厘秒），2 = ISO 8601 扩展格式。
    /// </summary>
    public static string Time(DateTime time, int timeFormat)
    {
        return timeFormat switch
        {
            0 => time.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture),
            2 => time.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            _ => time.ToString("yyyyMMddHHmmssff", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Host 给的时间（S2F31）：12 位、16 位、ISO 8601 都认。</summary>
    public static bool TryParseTime(string text, out DateTime time)
    {
        text = text.Trim();
        switch (text.Length)
        {
            case 12:
                return DateTime.TryParseExact(text, "yyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

            case 16:
                if (DateTime.TryParseExact(text[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
                    && int.TryParse(text[14..], NumberStyles.None, CultureInfo.InvariantCulture, out int centiseconds))
                {
                    time = time.AddMilliseconds(centiseconds * 10);
                    return true;
                }

                return false;

            default:
                return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out time);
        }
    }

    /// <summary>
    /// Host 给的 EC 值（S2F15 的 ECV）转成 ec.xml 的写法：整数、浮点、布尔、文字都认，格式对不上（给 Int 的发了文字这类）返回 null。
    /// 上下限、可选值由 EcComponent 再查。
    /// </summary>
    public static string? ToEcText(SecsItem value, string format)
    {
        try
        {
            switch (format)
            {
                case "Bool":
                    if (value.Format == SecsFormat.Boolean)
                    {
                        var flags = value.GetBooleanArray();
                        return flags.Length == 1 ? flags[0].ToString() : null;
                    }

                    if (value.Format is SecsFormat.Ascii or SecsFormat.Jis8)
                    {
                        return value.GetString().Trim();
                    }

                    var bits = value.Format == SecsFormat.Binary ? value.GetBinary().Select(bit => (long)bit).ToArray() : value.GetInt64Array();
                    return bits.Length == 1 ? (bits[0] != 0).ToString() : null;

                case "Int":
                    if (value.Format is SecsFormat.Ascii or SecsFormat.Jis8)
                    {
                        return value.GetString().Trim();
                    }

                    var numbers = value.GetInt64Array();
                    return numbers.Length == 1 ? numbers[0].ToString(CultureInfo.InvariantCulture) : null;

                case "Double":
                    if (value.Format is SecsFormat.Ascii or SecsFormat.Jis8)
                    {
                        return value.GetString().Trim();
                    }

                    if (value.Format is SecsFormat.F4 or SecsFormat.F8)
                    {
                        var reals = value.GetDoubleArray();
                        return reals.Length == 1 ? reals[0].ToString("R", CultureInfo.InvariantCulture) : null;
                    }

                    var integers = value.GetInt64Array();
                    return integers.Length == 1 ? integers[0].ToString(CultureInfo.InvariantCulture) : null;

                default:
                    return value.Format is SecsFormat.Ascii or SecsFormat.Jis8 ? value.GetString() : null;
            }
        }
        catch (Exception exception) when (exception is xyz.Secs.SecsException or OverflowException)
        {
            return null;
        }
    }
}
