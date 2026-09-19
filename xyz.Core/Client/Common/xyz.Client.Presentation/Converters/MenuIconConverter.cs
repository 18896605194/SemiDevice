using System;
using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace xyz.Client.Presentation.Converters;

/// <summary>
/// 菜单编码转底部导航图标：平台一级菜单按编码配图标，机型自己加的菜单用默认图标。
/// </summary>
public class MenuIconConverter : IValueConverter
{
    public static readonly MenuIconConverter Instance = new();

    private static readonly Dictionary<string, PackIconKind> Icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Main"] = PackIconKind.ViewDashboardOutline,
        ["Manual"] = PackIconKind.GestureTapButton,
        ["Recipe"] = PackIconKind.BookOpenPageVariantOutline,
        ["Alarm"] = PackIconKind.BellOutline,
        ["DataCenter"] = PackIconKind.DatabaseOutline,
        ["Setting"] = PackIconKind.CogOutline,
        ["Io"] = PackIconKind.ElectricSwitch,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string code && Icons.TryGetValue(code, out var kind)
            ? kind
            : PackIconKind.ApplicationOutline;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
