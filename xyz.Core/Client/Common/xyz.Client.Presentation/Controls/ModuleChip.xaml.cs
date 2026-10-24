using System.Windows;
using System.Windows.Controls;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 模块小框：一个圆点（状态）+ 模块名，边框亮灭表示生产 / 维护。顶栏模块行用它，别处要显示模块状态和生产 / 维护也用它
/// </summary>
public partial class ModuleChip : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ModuleChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(ModuleStateTone), typeof(ModuleChip), new PropertyMetadata(ModuleStateTone.Inactive));

    public static readonly DependencyProperty IsProductionProperty =
        DependencyProperty.Register(nameof(IsProduction), typeof(bool), typeof(ModuleChip), new PropertyMetadata(false));

    public static readonly DependencyProperty HasModeProperty =
        DependencyProperty.Register(nameof(HasMode), typeof(bool), typeof(ModuleChip), new PropertyMetadata(true));

    public ModuleChip()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 框里的字（模块名，machine.xml 原样）
    /// </summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// 状态色调：就绪圆点绿、报警红，其他灰
    /// </summary>
    public ModuleStateTone Tone
    {
        get => (ModuleStateTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>
    /// 生产模式：边框亮；维护（或不知道）边框灭
    /// </summary>
    public bool IsProduction
    {
        get => (bool)GetValue(IsProductionProperty);
        set => SetValue(IsProductionProperty, value);
    }

    /// <summary>
    /// 分不分生产 / 维护：不分的（供液）不画边框
    /// </summary>
    public bool HasMode
    {
        get => (bool)GetValue(HasModeProperty);
        set => SetValue(HasModeProperty, value);
    }
}
