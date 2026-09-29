using System.Windows;
using System.Windows.Controls;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 模块状态徽标：状态字 + 按色调上色的整条底色。LoadPort、Robot、腔体……所有模块的状态显示统一用它。
/// 没有状态字（没接状态的地方）整个收起，不留一条空的灰条。
/// </summary>
public partial class ModuleStateBadge : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ModuleStateBadge),
            new PropertyMetadata(string.Empty, (d, _) => ((ModuleStateBadge)d).UpdateVisibility()));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(ModuleStateTone), typeof(ModuleStateBadge), new PropertyMetadata(ModuleStateTone.Inactive));

    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(ModuleStateBadge), new PropertyMetadata(false));

    public ModuleStateBadge()
    {
        InitializeComponent();
        UpdateVisibility();
    }

    /// <summary>状态字（显示模型的 StateText，已按当前语言翻好）；空就不显示。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>缩小版：小卡片标题行里用，矮一截、字小、宽度随字。</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>色调（显示模型的 StateTone）。</summary>
    public ModuleStateTone Tone
    {
        get => (ModuleStateTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    private void UpdateVisibility()
    {
        Visibility = string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
