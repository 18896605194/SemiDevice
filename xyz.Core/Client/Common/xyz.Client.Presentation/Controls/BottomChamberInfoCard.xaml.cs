using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 标题栏位于底部的独立腔体信息卡片控件。
/// </summary>
public partial class BottomChamberInfoCard : UserControl
{
    public BottomChamberInfoCard()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(BottomChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public bool StatusOn
    {
        get => (bool)GetValue(StatusOnProperty);
        set => SetValue(StatusOnProperty, value);
    }

    public static readonly DependencyProperty StatusOnProperty =
        DependencyProperty.Register(nameof(StatusOn), typeof(bool), typeof(BottomChamberInfoCard),
            new PropertyMetadata(false));

    public string IsOnline
    {
        get => (string)GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public static readonly DependencyProperty IsOnlineProperty =
        DependencyProperty.Register(nameof(IsOnline), typeof(string), typeof(BottomChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string State
    {
        get => (string)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(string), typeof(BottomChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string RecipeState
    {
        get => (string)GetValue(RecipeStateProperty);
        set => SetValue(RecipeStateProperty, value);
    }

    public static readonly DependencyProperty RecipeStateProperty =
        DependencyProperty.Register(nameof(RecipeState), typeof(string), typeof(BottomChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string Recipe
    {
        get => (string)GetValue(RecipeProperty);
        set => SetValue(RecipeProperty, value);
    }

    public static readonly DependencyProperty RecipeProperty =
        DependencyProperty.Register(nameof(Recipe), typeof(string), typeof(BottomChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string Step
    {
        get => (string)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(string), typeof(BottomChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string StepTime
    {
        get => (string)GetValue(StepTimeProperty);
        set => SetValue(StepTimeProperty, value);
    }

    public static readonly DependencyProperty StepTimeProperty =
        DependencyProperty.Register(nameof(StepTime), typeof(string), typeof(BottomChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string RecipeTime
    {
        get => (string)GetValue(RecipeTimeProperty);
        set => SetValue(RecipeTimeProperty, value);
    }

    public static readonly DependencyProperty RecipeTimeProperty =
        DependencyProperty.Register(nameof(RecipeTime), typeof(string), typeof(BottomChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public string ShutterState
    {
        get => (string)GetValue(ShutterStateProperty);
        set => SetValue(ShutterStateProperty, value);
    }

    public static readonly DependencyProperty ShutterStateProperty =
        DependencyProperty.Register(nameof(ShutterState), typeof(string), typeof(BottomChamberInfoCard),
            new PropertyMetadata(string.Empty));

    public WaferModel? Wafer
    {
        get => (WaferModel?)GetValue(WaferProperty);
        set => SetValue(WaferProperty, value);
    }

    public static readonly DependencyProperty WaferProperty =
        DependencyProperty.Register(nameof(Wafer), typeof(WaferModel), typeof(BottomChamberInfoCard),
            new PropertyMetadata(null));

    public ICommand? CreateCommand
    {
        get => (ICommand?)GetValue(CreateCommandProperty);
        set => SetValue(CreateCommandProperty, value);
    }

    public static readonly DependencyProperty CreateCommandProperty =
        DependencyProperty.Register(nameof(CreateCommand), typeof(ICommand), typeof(BottomChamberInfoCard),
            new PropertyMetadata(null));

    public ICommand? DeleteCommand
    {
        get => (ICommand?)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public static readonly DependencyProperty DeleteCommandProperty =
        DependencyProperty.Register(nameof(DeleteCommand), typeof(ICommand), typeof(BottomChamberInfoCard),
            new PropertyMetadata(null));

    public bool CreateEnable
    {
        get => (bool)GetValue(CreateEnableProperty);
        set => SetValue(CreateEnableProperty, value);
    }

    public static readonly DependencyProperty CreateEnableProperty =
        DependencyProperty.Register(nameof(CreateEnable), typeof(bool), typeof(BottomChamberInfoCard),
            new PropertyMetadata(true));

    public bool DeleteEnable
    {
        get => (bool)GetValue(DeleteEnableProperty);
        set => SetValue(DeleteEnableProperty, value);
    }

    public static readonly DependencyProperty DeleteEnableProperty =
        DependencyProperty.Register(nameof(DeleteEnable), typeof(bool), typeof(BottomChamberInfoCard),
            new PropertyMetadata(true));

    public double RotationSpeed
    {
        get => (double)GetValue(RotationSpeedProperty);
        set => SetValue(RotationSpeedProperty, value);
    }

    public static readonly DependencyProperty RotationSpeedProperty =
        DependencyProperty.Register(nameof(RotationSpeed), typeof(double), typeof(BottomChamberInfoCard),
            new PropertyMetadata(0.0));
}

