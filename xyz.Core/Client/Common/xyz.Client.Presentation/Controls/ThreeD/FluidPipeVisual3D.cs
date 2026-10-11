using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 带流动效果的管子。局部直管沿 +X，末端沿 -Y 出液；DIW、SC1 各创建一个实例。
/// 作为 Arm.Attachments 的子项挂载，输入 IsFlowing 即可独立控制显示。
/// </summary>
public sealed class FluidPipeVisual3D : HardwareVisual3D
{
    private const double PipeRadius = 0.024;

    /// <summary>喷口比直管低多少：喷口在局部 (Length, -OutletDrop, 0)，装配层按它算液柱落点。</summary>
    public const double OutletDrop = 0.21;

    private readonly Model3DGroup _liquid = new();
    private readonly List<Trail> _trails = [];
    private readonly SolidColorBrush _rippleBrush = new();
    private readonly ScaleTransform3D _rippleScale = new();
    private GeometryModel3D _stream = null!;
    private GeometryModel3D _ripple = null!;
    private bool _clockRunning;

    static FluidPipeVisual3D()
    {
        HighlightColorProperty.OverrideMetadata(typeof(FluidPipeVisual3D), new PropertyMetadata(OnLiquidColorChanged));
    }

    public FluidPipeVisual3D()
    {
        RebuildGeometry();
    }

    protected override void OnSceneConnectionChanged()
    {
        // 显式重新寻找宿主：RelativeSource 绑定在整个三维子树移除后可能仍缓存旧视口。
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        while (parent is not null && parent is not Viewport3D)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        BindingOperations.ClearBinding(this, IsHostVisibleProperty);
        if (parent is Viewport3D viewport)
        {
            BindingOperations.SetBinding(this, IsHostVisibleProperty,
                new Binding(nameof(UIElement.IsVisible)) { Source = viewport, Mode = BindingMode.OneWay });
        }
        else
        {
            SetValue(IsHostVisibleProperty, false);
        }
    }

    public bool IsFlowing
    {
        get => (bool)GetValue(IsFlowingProperty);
        set => SetValue(IsFlowingProperty, value);
    }

    public static readonly DependencyProperty IsFlowingProperty = DependencyProperty.Register(
        nameof(IsFlowing), typeof(bool), typeof(FluidPipeVisual3D), new PropertyMetadata(false, OnFlowChanged));

    /// <summary>可选的动画总开关。关闭时保留出液状态和静态液柱，不改变 IsFlowing。</summary>
    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    public static readonly DependencyProperty IsAnimationEnabledProperty = DependencyProperty.Register(
        nameof(IsAnimationEnabled), typeof(bool), typeof(FluidPipeVisual3D), new PropertyMetadata(true, OnFlowChanged));

    /// <summary>直管显示长度，通常绑定 Arm.Length；与场景使用相同长度单位。</summary>
    public double Length
    {
        get => (double)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    public static readonly DependencyProperty LengthProperty = DependencyProperty.Register(
        nameof(Length), typeof(double), typeof(FluidPipeVisual3D), new PropertyMetadata(2.2d, OnGeometryChanged),
        value => value is double length && double.IsFinite(length) && length > 0);

    /// <summary>喷口到接液面的显示距离；0 不显示外部液柱。由装配层设置，不是流量。</summary>
    public double StreamLength
    {
        get => (double)GetValue(StreamLengthProperty);
        set => SetValue(StreamLengthProperty, value);
    }

    public static readonly DependencyProperty StreamLengthProperty = DependencyProperty.Register(
        nameof(StreamLength), typeof(double), typeof(FluidPipeVisual3D), new PropertyMetadata(0.55d, OnGeometryChanged),
        value => value is double length && double.IsFinite(length) && length >= 0);

    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        "Phase", typeof(double), typeof(FluidPipeVisual3D), new PropertyMetadata(0d, OnPhaseChanged));

    private static readonly DependencyProperty IsHostVisibleProperty = DependencyProperty.Register(
        "IsHostVisible", typeof(bool), typeof(FluidPipeVisual3D), new PropertyMetadata(false, OnFlowChanged));

    private static void OnFlowChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((FluidPipeVisual3D)sender).UpdateFlow();

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((FluidPipeVisual3D)sender).RebuildGeometry();

    private static void OnPhaseChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((FluidPipeVisual3D)sender).UpdateTrails();

    private static void OnLiquidColorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((FluidPipeVisual3D)sender).UpdateLiquidMaterials();

    private void RebuildGeometry()
    {
        ClearMeshes();
        _liquid.Children.Clear();
        _trails.Clear();
        var horizontal = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -90));
        horizontal.Freeze();
        AddMesh(HardwareMesh3D.Cylinder(PipeRadius, Length, 0), 1.12).Transform = horizontal;
        AddMesh(HardwareMesh3D.Cylinder(PipeRadius * 1.35, OutletDrop, -OutletDrop), 1.18)
            .Transform = new TranslateTransform3D(Length, 0, 0);

        // 静态液柱打底，上面叠加位置实际变化的亮段和液滴。
        _stream = new GeometryModel3D
        {
            Geometry = HardwareMesh3D.Cylinder(0.012, 1, 0),
            Transform = Group(new ScaleTransform3D(1, StreamLength, 1),
                new TranslateTransform3D(Length, -OutletDrop - StreamLength, 0))
        };
        if (StreamLength > 0)
        {
            _liquid.Children.Add(_stream);
        }

        AddTrails(Length, false, horizontal);
        if (StreamLength > 0)
        {
            AddTrails(StreamLength, true, Transform3D.Identity);
        }

        _ripple = new GeometryModel3D(HardwareMesh3D.Annulus(0.84, 1), new EmissiveMaterial(_rippleBrush))
        {
            Transform = Group(_rippleScale, new TranslateTransform3D(Length, -OutletDrop - StreamLength, 0))
        };
        if (StreamLength > 0)
        {
            _liquid.Children.Add(_ripple);
        }
        UpdateLiquidMaterials();
        UpdateFlow();
    }

    private void AddTrails(double distance, bool falling, Transform3D orientation)
    {
        int count = (int)Math.Clamp(Math.Ceiling(distance / (falling ? 0.17 : 0.52)), 1, 64);
        var mesh = HardwareMesh3D.Cylinder(falling ? 0.02 : PipeRadius * 1.2, 1, 0);
        for (int i = 0; i < count + 1; i++)
        {
            var scale = new ScaleTransform3D();
            var translation = new TranslateTransform3D();
            var part = new GeometryModel3D { Geometry = mesh, Transform = Group(scale, orientation, translation) };
            _trails.Add(new Trail(part, scale, translation, distance, i, count, falling));
            _liquid.Children.Add(part);
        }
    }

    private static Transform3DGroup Group(params Transform3D[] transforms)
    {
        var result = new Transform3DGroup();
        foreach (var transform in transforms)
        {
            result.Children.Add(transform);
        }

        return result;
    }

    private void UpdateFlow()
    {
        SetVisualActive(IsFlowing);
        if (IsFlowing && !Model.Children.Contains(_liquid))
        {
            Model.Children.Add(_liquid);
        }

        if (!IsFlowing)
        {
            Model.Children.Remove(_liquid);
        }

        bool run = IsFlowing && IsAnimationEnabled && (bool)GetValue(IsHostVisibleProperty);
        if (run != _clockRunning)
        {
            _clockRunning = run;
            if (run)
            {
                var animation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(animation, 30);
                BeginAnimation(PhaseProperty, animation);
            }
            else
            {
                BeginAnimation(PhaseProperty, null);
            }
        }
        UpdateTrails();
    }

    private void UpdateTrails()
    {
        double phase = (double)GetValue(PhaseProperty);
        foreach (var trail in _trails)
        {
            double spacing = trail.Distance / trail.Count;
            double position = (phase * (trail.Falling ? 6 : 3) % 1 + trail.Index - 1) * spacing;
            double start = Math.Max(0, position), end = Math.Min(trail.Distance, position + spacing * 0.32);
            trail.Scale.ScaleY = Math.Max(0, end - start);
            trail.Translation.OffsetX = trail.Falling ? Length : start;
            trail.Translation.OffsetY = trail.Falling ? -OutletDrop - end : 0;
        }
        double ripplePhase = phase * 4 % 1;
        double radius = 0.025 + ripplePhase * 0.13;
        _rippleScale.ScaleX = _rippleScale.ScaleZ = radius;
        Color accent = HighlightColor;
        _rippleBrush.Color = Color.FromArgb((byte)(150 * (1 - ripplePhase)), accent.R, accent.G, accent.B);
    }

    private void UpdateLiquidMaterials()
    {
        if (_stream is null)
        {
            return;
        }

        var streamMaterial = new DiffuseMaterial(new SolidColorBrush(HighlightColor));
        streamMaterial.Freeze();
        _stream.Material = streamMaterial;
        Color color = HighlightColor;
        byte Light(byte value) => (byte)(value * 0.45 + 255 * 0.55);
        var trailMaterial = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(Light(color.R), Light(color.G), Light(color.B))));
        trailMaterial.Freeze();
        foreach (var trail in _trails)
        {
            trail.Model.Material = trailMaterial;
        }

        UpdateTrails();
    }

    private sealed record Trail(GeometryModel3D Model, ScaleTransform3D Scale, TranslateTransform3D Translation,
        double Distance, int Index, int Count, bool Falling);
}
