using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// Arm 上的一路喷嘴：透明供液管、向下喷口和外部液柱。
/// 与普通 FluidPipe 控件共用 FluidRouteVisual3D；DIW、SC1 各自绑定 IsFlowing。
/// </summary>
public sealed class FluidPipeVisual3D : HardwareVisual3D
{
    private const double TubeRadius = 0.032;
    private const double TubeWallThickness = 0.008;
    private const double LiquidRadius = (TubeRadius - TubeWallThickness) * 0.58;

    /// <summary>喷口在局部 (Length, -OutletDrop, 0)，装配层按此位置计算接液面。</summary>
    public const double OutletDrop = 0.21;

    private readonly FluidRouteVisual3D _tube = new()
    {
        PipeRadius = TubeRadius,
        WallThickness = TubeWallThickness,
        BendRadius = 0.09
    };

    // 外部液柱继续使用同一套水纹，管壁透明度设为 0，只显示液体。
    private readonly FluidRouteVisual3D _jet = new()
    {
        PipeRadius = LiquidRadius + 0.0001,
        WallThickness = 0.0001,
        FluidCoreRatio = 1,
        BendRadius = 0,
        WallOpacity = 0,
        FlowSpeed = 0.9
    };

    private readonly SolidColorBrush _rippleBrush = new();
    private readonly ScaleTransform3D _rippleScale = new();
    private readonly TranslateTransform3D _ripplePosition = new();
    private readonly GeometryModel3D _ripple;
    private bool _clockRunning;

    public FluidPipeVisual3D()
    {
        var rippleTransform = new Transform3DGroup();
        rippleTransform.Children.Add(_rippleScale);
        rippleTransform.Children.Add(_ripplePosition);
        _ripple = new GeometryModel3D(
            HardwareMesh3D.Annulus(0.86, 1),
            new EmissiveMaterial(_rippleBrush))
        {
            Transform = rippleTransform
        };

        Children.Add(_tube);
        Children.Add(_jet);
        UpdateTubePath();
        UpdateJetPath();
        UpdateAppearance();
        UpdateFlow();
    }

    /// <summary>本路喷嘴是否出液。关闭时管内液芯、外部液柱和落点波纹全部消失。</summary>
    public bool IsFlowing
    {
        get
        {
            return (bool)GetValue(IsFlowingProperty);
        }
        set
        {
            SetValue(IsFlowingProperty, value);
        }
    }

    public static readonly DependencyProperty IsFlowingProperty = DependencyProperty.Register(
        nameof(IsFlowing), typeof(bool), typeof(FluidPipeVisual3D),
        new PropertyMetadata(false, OnFlowChanged));

    /// <summary>暂停动画但保留出液状态。</summary>
    public bool IsAnimationEnabled
    {
        get
        {
            return (bool)GetValue(IsAnimationEnabledProperty);
        }
        set
        {
            SetValue(IsAnimationEnabledProperty, value);
        }
    }

    public static readonly DependencyProperty IsAnimationEnabledProperty = DependencyProperty.Register(
        nameof(IsAnimationEnabled), typeof(bool), typeof(FluidPipeVisual3D),
        new PropertyMetadata(true, OnFlowChanged));

    /// <summary>沿 Arm 的局部 +X 方向布管，长度与场景使用同一单位。</summary>
    public double Length
    {
        get
        {
            return (double)GetValue(LengthProperty);
        }
        set
        {
            SetValue(LengthProperty, value);
        }
    }

    public static readonly DependencyProperty LengthProperty = DependencyProperty.Register(
        nameof(Length), typeof(double), typeof(FluidPipeVisual3D),
        new PropertyMetadata(2.2d, OnLengthChanged), IsPositiveFinite);

    /// <summary>喷口到接液面的距离；0 时只有管内水流，不显示外部液柱和波纹。</summary>
    public double StreamLength
    {
        get
        {
            return (double)GetValue(StreamLengthProperty);
        }
        set
        {
            SetValue(StreamLengthProperty, value);
        }
    }

    public static readonly DependencyProperty StreamLengthProperty = DependencyProperty.Register(
        nameof(StreamLength), typeof(double), typeof(FluidPipeVisual3D),
        new PropertyMetadata(0.55d, OnStreamLengthChanged), IsNonNegativeFinite);

    /// <summary>管内水流、喷口液柱和落点波纹使用同一种颜色。</summary>
    public Color FlowColor
    {
        get
        {
            return (Color)GetValue(FlowColorProperty);
        }
        set
        {
            SetValue(FlowColorProperty, value);
        }
    }

    public static readonly DependencyProperty FlowColorProperty = DependencyProperty.Register(
        nameof(FlowColor), typeof(Color), typeof(FluidPipeVisual3D),
        new PropertyMetadata(Color.FromRgb(82, 212, 239), OnAppearanceChanged));

    /// <summary>透明供液管的管壁不透明度强度。</summary>
    public double WallOpacity
    {
        get
        {
            return (double)GetValue(WallOpacityProperty);
        }
        set
        {
            SetValue(WallOpacityProperty, value);
        }
    }

    public static readonly DependencyProperty WallOpacityProperty = DependencyProperty.Register(
        nameof(WallOpacity), typeof(double), typeof(FluidPipeVisual3D),
        new PropertyMetadata(0.18d, OnAppearanceChanged), IsOpacity);

    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        "Phase", typeof(double), typeof(FluidPipeVisual3D),
        new PropertyMetadata(0d, OnPhaseChanged));

    private static readonly DependencyProperty IsHostVisibleProperty = DependencyProperty.Register(
        "IsHostVisible", typeof(bool), typeof(FluidPipeVisual3D),
        new PropertyMetadata(false, OnFlowChanged));

    protected override void OnSceneConnectionChanged()
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        while (parent != null && parent is not Viewport3D)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        BindingOperations.ClearBinding(this, IsHostVisibleProperty);
        if (parent is Viewport3D viewport)
        {
            var binding = new Binding(nameof(UIElement.IsVisible))
            {
                Source = viewport,
                Mode = BindingMode.OneWay
            };
            BindingOperations.SetBinding(this, IsHostVisibleProperty, binding);
        }
        else
        {
            SetValue(IsHostVisibleProperty, false);
        }
    }

    private static bool IsPositiveFinite(object value)
    {
        double number = (double)value;
        return double.IsFinite(number) && number > 0;
    }

    private static bool IsNonNegativeFinite(object value)
    {
        double number = (double)value;
        return double.IsFinite(number) && number >= 0;
    }

    private static bool IsOpacity(object value)
    {
        double number = (double)value;
        return double.IsFinite(number) && number >= 0 && number <= 1;
    }

    private static void OnLengthChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidPipeVisual3D)sender;
        pipe.UpdateTubePath();
        pipe.UpdateJetPath();
    }

    private static void OnStreamLengthChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidPipeVisual3D)sender;
        pipe.UpdateJetPath();
        pipe.UpdateFlow();
    }

    private static void OnFlowChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidPipeVisual3D)sender;
        pipe.UpdateFlow();
    }

    private static void OnAppearanceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidPipeVisual3D)sender;
        pipe.UpdateAppearance();
    }

    private static void OnPhaseChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidPipeVisual3D)sender;
        pipe.UpdateRipple();
    }

    private void UpdateTubePath()
    {
        var path = new Point3DCollection
        {
            new Point3D(0, 0, 0),
            new Point3D(Length, 0, 0),
            new Point3D(Length, -OutletDrop, 0)
        };
        path.Freeze();
        _tube.Path = path;
    }

    private void UpdateJetPath()
    {
        var path = new Point3DCollection();
        if (StreamLength > 0)
        {
            path.Add(new Point3D(Length, -OutletDrop, 0));
            path.Add(new Point3D(Length, -OutletDrop - StreamLength, 0));
        }

        path.Freeze();
        _jet.Path = path;
        _ripplePosition.OffsetX = Length;
        _ripplePosition.OffsetY = -OutletDrop - StreamLength;
    }

    private void UpdateAppearance()
    {
        _tube.FlowColor = FlowColor;
        _tube.WallOpacity = WallOpacity;
        _jet.FlowColor = FlowColor;
        UpdateRipple();
    }

    private void UpdateFlow()
    {
        _tube.IsFlowing = IsFlowing;
        _tube.IsAnimationEnabled = IsAnimationEnabled;
        _jet.IsFlowing = IsFlowing && StreamLength > 0;
        _jet.IsAnimationEnabled = IsAnimationEnabled;

        Model.Children.Clear();
        if (IsFlowing && StreamLength > 0)
        {
            Model.Children.Add(_ripple);
        }

        bool shouldRun = IsFlowing && IsAnimationEnabled && (bool)GetValue(IsHostVisibleProperty);
        if (shouldRun != _clockRunning)
        {
            double phase = (double)GetValue(PhaseProperty) % 1;
            BeginAnimation(PhaseProperty, null);
            SetValue(PhaseProperty, phase);
            _clockRunning = shouldRun;

            if (shouldRun)
            {
                var animation = new DoubleAnimation(phase, phase + 1, TimeSpan.FromSeconds(1.6))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                Timeline.SetDesiredFrameRate(animation, 30);
                BeginAnimation(PhaseProperty, animation);
            }
        }

        UpdateRipple();
    }

    private void UpdateRipple()
    {
        double phase = (double)GetValue(PhaseProperty) * 4 % 1;
        double radius = 0.025 + phase * 0.10;
        _rippleScale.ScaleX = radius;
        _rippleScale.ScaleZ = radius;
        _rippleBrush.Color = Color.FromArgb(
            (byte)(85 * (1 - phase)), FlowColor.R, FlowColor.G, FlowColor.B);
    }
}
