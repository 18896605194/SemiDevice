using System.Globalization;
using System.Windows.Data;

namespace CdsLayoutPreview;

public sealed class BooleanTextConverter : IValueConverter
{
    public static IReadOnlyList<string> Choices { get; } = ["true", "false"];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "true" : "false";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string text && bool.TryParse(text, out bool result))
        {
            return result;
        }
        return Binding.DoNothing;
    }
}
