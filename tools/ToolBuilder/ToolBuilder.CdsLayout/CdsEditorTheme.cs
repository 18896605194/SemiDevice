using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Media;
using xyz.Client.DataModels.ViewModels;

namespace CdsAssemblyPreview;

public static class CdsEditorServices
{
    public static IServiceCollection AddCdsLayoutEditor(this IServiceCollection services)
    {
        // 每个药液模块持有自己的编辑状态，切换模块不互相覆盖。
        services.AddTransient<PreviewViewModel>();
        services.AddTransient<BaseViewModel>(sp => sp.GetRequiredService<PreviewViewModel>());
        services.AddTransient(_ => new PreviewPage(embedded: true));
        return services;
    }
}

public static class CdsEditorTheme
{
    public static ResourceDictionary CreateEmbedded()
    {
        var theme = new ResourceDictionary();
        foreach (string name in new[] { "DarkColors", "FontSize" })
            theme.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/xyz.Client.Presentation;component/Styles/{name}.xaml") });
        foreach (var (key, color) in new Dictionary<string, string>
        {
            ["DarkWindowBackground"] = "#FFFFFF", ["DarkSurfaceBackground"] = "#F8FAFD", ["DarkControlBackground"] = "#FFFFFF",
            ["DarkBorderBrush"] = "#D5DDE9", ["DarkPrimaryText"] = "#23324A", ["DarkSecondaryText"] = "#63738C",
            ["DarkDisabledText"] = "#63738C", ["DarkAccent"] = "#2463EB", ["DarkAccentSoft"] = "#EAF1FF", ["DarkAccentPressed"] = "#DFEBFF"
        }) theme[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        foreach (string resource in new[] { "CommonEditorStyles", "EmbeddedStyles", "Editor/Strings.zh-CN", "Editor/Hardware/HardwareStrings.zh-CN", "Editor/Hardware/Pipeline/PipelineStrings.zh-CN", "Editor/PreviewStyles" })
            theme.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/ToolBuilder.CdsLayout;component/{resource}.xaml") });
        return theme;
    }
}
