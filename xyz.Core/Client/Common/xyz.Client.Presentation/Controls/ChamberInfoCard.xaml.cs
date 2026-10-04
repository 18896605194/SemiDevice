using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 近方形腔体信息卡片，参考 GR 调度界面的 ChamberInfoCard：标题行带缩小版状态徽标，左五行字段 + 右圆片。按真实字号排版、随给定尺寸伸缩，不整卡缩放。
/// 字段哪一行没有值就整行不显示（现在后端只给配方，步骤、腔门、时间这些有了数据才出来）。
/// </summary>
public partial class ChamberInfoCard : UserControl
{
    public ChamberInfoCard()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// 站点号角标（机械手站点表里的设备站点号）；空就不显示。
    /// </summary>
    public string Number
    {
        get => (string)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public static readonly DependencyProperty NumberProperty =
        DependencyProperty.Register(nameof(Number), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// 圆片直径：跟调度图里机械手叉上的片一样大，由页面按机械手的显示尺寸给。
    /// </summary>
    public double DiskSize
    {
        get => (double)GetValue(DiskSizeProperty);
        set => SetValue(DiskSizeProperty, value);
    }

    public static readonly DependencyProperty DiskSizeProperty =
        DependencyProperty.Register(nameof(DiskSize), typeof(double), typeof(ChamberInfoCard),
            new PropertyMetadata(100.0));

    public bool StatusOn
    {
        get => (bool)GetValue(StatusOnProperty);
        set => SetValue(StatusOnProperty, value);
    }

    public static readonly DependencyProperty StatusOnProperty =
        DependencyProperty.Register(nameof(StatusOn), typeof(bool), typeof(ChamberInfoCard),
            new PropertyMetadata(false));

    public string IsOnline
    {
        get => (string)GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public static readonly DependencyProperty IsOnlineProperty =
        DependencyProperty.Register(nameof(IsOnline), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// 腔体状态文字，标题行右侧缩小版状态徽标显示；空就不显示徽标。
    /// </summary>
    public string StateText
    {
        get => (string)GetValue(StateTextProperty);
        set => SetValue(StateTextProperty, value);
    }

    public static readonly DependencyProperty StateTextProperty =
        DependencyProperty.Register(nameof(StateText), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// 腔体状态色调（徽标底色）。
    /// </summary>
    public ModuleStateTone StateTone
    {
        get => (ModuleStateTone)GetValue(StateToneProperty);
        set => SetValue(StateToneProperty, value);
    }

    public static readonly DependencyProperty StateToneProperty =
        DependencyProperty.Register(nameof(StateTone), typeof(ModuleStateTone), typeof(ChamberInfoCard),
            new PropertyMetadata(ModuleStateTone.Inactive));

    public string RecipeState
    {
        get => (string)GetValue(RecipeStateProperty);
        set => SetValue(RecipeStateProperty, value);
    }

    public static readonly DependencyProperty RecipeStateProperty =
        DependencyProperty.Register(nameof(RecipeState), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string Recipe
    {
        get => (string)GetValue(RecipeProperty);
        set => SetValue(RecipeProperty, value);
    }

    public static readonly DependencyProperty RecipeProperty =
        DependencyProperty.Register(nameof(Recipe), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string Step
    {
        get => (string)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string StepTime
    {
        get => (string)GetValue(StepTimeProperty);
        set => SetValue(StepTimeProperty, value);
    }

    public static readonly DependencyProperty StepTimeProperty =
        DependencyProperty.Register(nameof(StepTime), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string RecipeTime
    {
        get => (string)GetValue(RecipeTimeProperty);
        set => SetValue(RecipeTimeProperty, value);
    }

    public static readonly DependencyProperty RecipeTimeProperty =
        DependencyProperty.Register(nameof(RecipeTime), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string ShutterState
    {
        get => (string)GetValue(ShutterStateProperty);
        set => SetValue(ShutterStateProperty, value);
    }

    public static readonly DependencyProperty ShutterStateProperty =
        DependencyProperty.Register(nameof(ShutterState), typeof(string), typeof(ChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public WaferModel? Wafer
    {
        get => (WaferModel?)GetValue(WaferProperty);
        set => SetValue(WaferProperty, value);
    }

    public static readonly DependencyProperty WaferProperty =
        DependencyProperty.Register(nameof(Wafer), typeof(WaferModel), typeof(ChamberInfoCard),
            new PropertyMetadata(null));

    public ICommand? CreateCommand
    {
        get => (ICommand?)GetValue(CreateCommandProperty);
        set => SetValue(CreateCommandProperty, value);
    }

    public static readonly DependencyProperty CreateCommandProperty =
        DependencyProperty.Register(nameof(CreateCommand), typeof(ICommand), typeof(ChamberInfoCard),
            new PropertyMetadata(null));

    public ICommand? DeleteCommand
    {
        get => (ICommand?)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public static readonly DependencyProperty DeleteCommandProperty =
        DependencyProperty.Register(nameof(DeleteCommand), typeof(ICommand), typeof(ChamberInfoCard),
            new PropertyMetadata(null));

    public bool CreateEnable
    {
        get => (bool)GetValue(CreateEnableProperty);
        set => SetValue(CreateEnableProperty, value);
    }

    public static readonly DependencyProperty CreateEnableProperty =
        DependencyProperty.Register(nameof(CreateEnable), typeof(bool), typeof(ChamberInfoCard),
            new PropertyMetadata(true));

    public bool DeleteEnable
    {
        get => (bool)GetValue(DeleteEnableProperty);
        set => SetValue(DeleteEnableProperty, value);
    }

    public static readonly DependencyProperty DeleteEnableProperty =
        DependencyProperty.Register(nameof(DeleteEnable), typeof(bool), typeof(ChamberInfoCard),
            new PropertyMetadata(true));

    public double RotationSpeed
    {
        get => (double)GetValue(RotationSpeedProperty);
        set => SetValue(RotationSpeedProperty, value);
    }

    public static readonly DependencyProperty RotationSpeedProperty =
        DependencyProperty.Register(nameof(RotationSpeed), typeof(double), typeof(ChamberInfoCard),
            new PropertyMetadata(0.0));
}
