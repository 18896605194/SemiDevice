using System;
using System.Globalization;
using System.Windows.Data;

namespace xyz.Client.Presentation.Converters;

/// <summary>
/// 对象不为空转布尔值转换器。
/// </summary>
public class ObjectNotNullToBooleanConverter : IValueConverter
{
    public static readonly ObjectNotNullToBooleanConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
