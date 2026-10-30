using System.Windows;
using System.Windows.Media;

namespace ChamberLayoutEditor;

internal static class ChamberTheme
{
    public static ResourceDictionary Create()
    {
        var theme = new ResourceDictionary();
        foreach (string name in new[] { "Styles/DarkColors", "Styles/FontSize" })
            theme.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/xyz.Client.Presentation;component/{name}.xaml") });
        theme.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ToolBuilder.ChamberLayout;component/Localization/Strings.zh-CN.xaml") });
        theme.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ToolBuilder.ChamberLayout;component/EditorStyles.xaml") });
        foreach (var (key, color) in new Dictionary<string, string>
        {
            ["DarkWindowBackground"] = "#FFFFFF", ["DarkSurfaceBackground"] = "#F8FAFD", ["DarkControlBackground"] = "#FFFFFF",
            ["DarkBorderBrush"] = "#D5DDE9", ["DarkPrimaryText"] = "#23324A", ["DarkSecondaryText"] = "#63738C",
            ["DarkDisabledText"] = "#8491A3", ["DarkAccent"] = "#2463EB", ["DarkAccentPressed"] = "#DFEBFF", ["DarkAccentSoft"] = "#EAF1FF"
        }) theme[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        return theme;
    }
}
