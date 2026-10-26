using System.Globalization;

namespace xyz.Common;

/// <summary>
/// 值换算：数据类型之间转、字符串按类型解析（后端用）
/// </summary>
public static class ValueHelper
{
    /// <summary>
    /// 双精度转单精度，空的还是空
    /// </summary>
    public static float? ToFloat(double? value)
    {
        if (value is null)
        {
            return null;
        }

        return (float)value.Value;
    }

    /// <summary>
    /// 对象转双精度：开关量 1 / 0，枚举取它的数，别的数字照转；转不了或不是有限数为空
    /// </summary>
    public static double? ToDouble(object? value)
    {
        double? number = null;
        if (value is bool flag)
        {
            number = flag ? 1 : 0;
        }
        else if (value is Enum member)
        {
            number = Convert.ToDouble(member, CultureInfo.InvariantCulture);
        }
        else if (value is IConvertible convertible && value is not string)
        {
            try
            {
                number = convertible.ToDouble(CultureInfo.InvariantCulture);
            }
            catch (InvalidCastException)
            {
                // 时间、字符这类实现了 IConvertible 却转不成数
                return null;
            }
        }

        if (number is null || !double.IsFinite(number.Value))
        {
            return null;
        }

        return number.Value;
    }

    /// <summary>
    /// 按不变区域性解析小数，不跟系统的区域设置走
    /// </summary>
    public static bool TryParseDouble(string? text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 字符串按类型转值：整数、小数、开关、字符串、枚举（枚举不分大小写）
    /// </summary>
    public static object ConvertTo(Type type, string text)
    {
        if (type == typeof(int))
        {
            return int.Parse(text, CultureInfo.InvariantCulture);
        }

        if (type == typeof(double))
        {
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        if (type == typeof(bool))
        {
            return bool.Parse(text);
        }

        if (type == typeof(string))
        {
            return text;
        }

        if (type.IsEnum)
        {
            return Enum.Parse(type, text, ignoreCase: true);
        }

        throw new NotSupportedException($"不支持把字符串转成 {type.Name}");
    }
}
