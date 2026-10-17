using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 按路径绘制的透明空心管路，可用于腔体、Tank 和 CDS。
/// 外部通过依赖属性提供路径、外观和流动状态，控件只负责显示。
/// </summary>
public sealed class FluidRouteVisual3D : HardwareVisual3D
{
    private readonly GeometryModel3D _backWall = new();
    private readonly GeometryModel3D _innerWall = new();
    private readonly GeometryModel3D _frontWall = new();
    private readonly GeometryModel3D _endRims = new();
    private readonly GeometryModel3D _fluidCore = new();
    private readonly TranslateTransform _flowOffset = new();

    private PathChangeListener? _pathListener;
    private bool _isAnimationRunning;
    private double _animationSpeed;
    private bool _hasRoute;

    public FluidRouteVisual3D()
    {
        // 集合不能放在依赖属性的默认值中，否则多个控件会共用同一条路径。
        SetCurrentValue(PathProperty, new Point3DCollection());
    }

    /// <summary>管中心的路径点，使用场景统一长度单位。替换集合或修改集合中的点都会重画管路。</summary>
    public Point3DCollection? Path
    {
        get
        {
            return (Point3DCollection?)GetValue(PathProperty);
        }
        set
        {
            SetValue(PathProperty, value);
        }
    }

    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path),
        typeof(Point3DCollection),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(null, OnPathChanged));

    /// <summary>管外半径，必须大于 0。</summary>
    public double PipeRadius
    {
        get
        {
            return (double)GetValue(PipeRadiusProperty);
        }
        set
        {
            SetValue(PipeRadiusProperty, value);
        }
    }

    public static readonly DependencyProperty PipeRadiusProperty = DependencyProperty.Register(
        nameof(PipeRadius),
        typeof(double),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(0.06d, OnGeometryChanged),
        IsPositiveFinite);

    /// <summary>管壁厚度。实际厚度最多取外半径的 90%，始终保留空腔。</summary>
    public double WallThickness
    {
        get
        {
            return (double)GetValue(WallThicknessProperty);
        }
        set
        {
            SetValue(WallThicknessProperty, value);
        }
    }

    public static readonly DependencyProperty WallThicknessProperty = DependencyProperty.Register(
        nameof(WallThickness),
        typeof(double),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(0.012d, OnGeometryChanged),
        IsPositiveFinite);

    /// <summary>液芯半径与内腔半径的比值，范围为 (0, 1]。用于留出透明间隙，不表示真实充液率。</summary>
    public double FluidCoreRatio
    {
        get
        {
            return (double)GetValue(FluidCoreRatioProperty);
        }
        set
        {
            SetValue(FluidCoreRatioProperty, value);
        }
    }

    public static readonly DependencyProperty FluidCoreRatioProperty = DependencyProperty.Register(
        nameof(FluidCoreRatio),
        typeof(double),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(0.58d, OnGeometryChanged),
        IsPositiveRatio);

    /// <summary>弯头圆角半径。短管段会自动缩小圆角，0 表示保留折线。</summary>
    public double BendRadius
    {
        get
        {
            return (double)GetValue(BendRadiusProperty);
        }
        set
        {
            SetValue(BendRadiusProperty, value);
        }
    }

    public static readonly DependencyProperty BendRadiusProperty = DependencyProperty.Register(
        nameof(BendRadius),
        typeof(double),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(0.22d, OnGeometryChanged),
        IsNonNegativeFinite);

    /// <summary>管壁不透明度强度，范围为 [0, 1]，默认 0.18。内外壁按比例使用此值，0 时隐藏管壁。</summary>
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
        nameof(WallOpacity),
        typeof(double),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(0.18d, OnMaterialChanged),
        IsOpacity);

    /// <summary>透明管壁的颜色，默认使用中性灰。</summary>
    public Color WallColor
    {
        get
        {
            return (Color)GetValue(WallColorProperty);
        }
        set
        {
            SetValue(WallColorProperty, value);
        }
    }

    public static readonly DependencyProperty WallColorProperty = DependencyProperty.Register(
        nameof(WallColor),
        typeof(Color),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(Color.FromRgb(180, 180, 180), OnMaterialChanged));

    /// <summary>液体底色。液芯颜色由 FlowColor 和此颜色按 3:1 混合。</summary>
    public Color FluidColor
    {
        get
        {
            return (Color)GetValue(FluidColorProperty);
        }
        set
        {
            SetValue(FluidColorProperty, value);
        }
    }

    public static readonly DependencyProperty FluidColorProperty = DependencyProperty.Register(
        nameof(FluidColor),
        typeof(Color),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(Color.FromRgb(196, 196, 196), OnMaterialChanged));

    /// <summary>水流主色，同时用于液芯着色和移动水纹，默认青蓝色。</summary>
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
        nameof(FlowColor),
        typeof(Color),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(Color.FromRgb(82, 212, 239), OnMaterialChanged));

    /// <summary>管内是否有液体。只有 IsFilled 和 IsFlowing 都为 true 时显示液芯。</summary>
    public bool IsFilled
    {
        get
        {
            return (bool)GetValue(IsFilledProperty);
        }
        set
        {
            SetValue(IsFilledProperty, value);
        }
    }

    public static readonly DependencyProperty IsFilledProperty = DependencyProperty.Register(
        nameof(IsFilled),
        typeof(bool),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(true, OnFlowChanged));

    /// <summary>是否正在流动。false 时移除液芯和水纹，只显示透明管壁；不修改 IsFilled。</summary>
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
        nameof(IsFlowing),
        typeof(bool),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(false, OnFlowChanged));

    /// <summary>动画速度，单位为场景长度/秒。0 表示暂停水纹；只是显示参数，不代表实测流量。</summary>
    public double FlowSpeed
    {
        get
        {
            return (double)GetValue(FlowSpeedProperty);
        }
        set
        {
            SetValue(FlowSpeedProperty, value);
        }
    }

    public static readonly DependencyProperty FlowSpeedProperty = DependencyProperty.Register(
        nameof(FlowSpeed),
        typeof(double),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(0.55d, OnAnimationChanged),
        IsNonNegativeFinite);

    /// <summary>是否反向流动。默认从 Path 的第一个点流向最后一个点。</summary>
    public bool ReverseFlow
    {
        get
        {
            return (bool)GetValue(ReverseFlowProperty);
        }
        set
        {
            SetValue(ReverseFlowProperty, value);
        }
    }

    public static readonly DependencyProperty ReverseFlowProperty = DependencyProperty.Register(
        nameof(ReverseFlow),
        typeof(bool),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(false, OnDirectionChanged));

    /// <summary>是否播放动画。false 时冻结水纹，保留当前液芯和流动状态。</summary>
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
        nameof(IsAnimationEnabled),
        typeof(bool),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(true, OnAnimationChanged));

    // 动画只改变纹理偏移，不在每一帧重新创建管路网格。
    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        "Phase",
        typeof(double),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(0d, OnPhaseChanged));

    private static readonly DependencyProperty IsHostVisibleProperty = DependencyProperty.Register(
        "IsHostVisible",
        typeof(bool),
        typeof(FluidRouteVisual3D),
        new PropertyMetadata(false, OnAnimationChanged));

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
            var visibilityBinding = new Binding(nameof(UIElement.IsVisible))
            {
                Source = viewport,
                Mode = BindingMode.OneWay
            };
            BindingOperations.SetBinding(this, IsHostVisibleProperty, visibilityBinding);
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

    private static bool IsPositiveRatio(object value)
    {
        double number = (double)value;
        return double.IsFinite(number) && number > 0 && number <= 1;
    }

    private static bool IsOpacity(object value)
    {
        double number = (double)value;
        return double.IsFinite(number) && number >= 0 && number <= 1;
    }

    private static void OnPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidRouteVisual3D)sender;
        if (ReferenceEquals(args.OldValue, args.NewValue))
        {
            return;
        }

        // 显式监听点集合，绑定后的单点修改也能刷新。
        pipe._pathListener?.Detach();
        pipe._pathListener = null;
        if (args.NewValue is Point3DCollection newPath && !newPath.IsFrozen)
        {
            pipe._pathListener = new PathChangeListener(pipe, newPath);
        }

        pipe.RebuildGeometry();
    }

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidRouteVisual3D)sender;
        pipe.RebuildGeometry();
    }

    private static void OnMaterialChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidRouteVisual3D)sender;
        pipe.UpdateMaterials();
    }

    private static void OnFlowChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidRouteVisual3D)sender;
        pipe.UpdateVisibleLayers();
        pipe.UpdateAnimation();
    }

    private static void OnAnimationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidRouteVisual3D)sender;
        pipe.UpdateAnimation();
    }

    private static void OnPhaseChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidRouteVisual3D)sender;
        pipe.UpdateFlowOffset();
    }

    private static void OnDirectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidRouteVisual3D)sender;
        pipe.UpdateFlowOffset();
    }

    private void RebuildGeometry()
    {
        var geometry = new FluidRouteGeometry(Path, BendRadius);
        _hasRoute = !geometry.IsEmpty;

        double innerRadius = Math.Max(PipeRadius * 0.1, PipeRadius - WallThickness);
        MeshGeometry3D outerMesh = geometry.CreateTube(PipeRadius);
        _frontWall.Geometry = outerMesh;
        _backWall.Geometry = outerMesh;
        _innerWall.Geometry = geometry.CreateTube(innerRadius, inward: true);
        _endRims.Geometry = geometry.CreateRims(PipeRadius, innerRadius);
        _fluidCore.Geometry = geometry.CreateTube(innerRadius * FluidCoreRatio, caps: true);

        UpdateMaterials();
        UpdateVisibleLayers();
        UpdateAnimation();
    }

    private void UpdateMaterials()
    {
        _backWall.Material = null;
        _backWall.BackMaterial = CreateSurface(WallColor, WallOpacity * 0.24, false);
        _innerWall.Material = CreateSurface(WallColor, WallOpacity * 0.08, false);
        _frontWall.Material = CreateSurface(WallColor, WallOpacity * 0.55, true);

        Material rimMaterial = CreateSurface(WallColor, Math.Min(1, WallOpacity * 2.2), true);
        _endRims.Material = rimMaterial;
        _endRims.BackMaterial = rimMaterial;

        Color coreColor = Color.FromRgb(
            (byte)((FlowColor.R * 3 + FluidColor.R) / 4),
            (byte)((FlowColor.G * 3 + FluidColor.G) / 4),
            (byte)((FlowColor.B * 3 + FluidColor.B) / 4));

        var fluidMaterial = new MaterialGroup();
        fluidMaterial.Children.Add(CreateSurface(coreColor, 0.46, true));
        fluidMaterial.Children.Add(new EmissiveMaterial(CreateFlowBrush()));
        _fluidCore.Material = fluidMaterial;
        _fluidCore.BackMaterial = fluidMaterial;
    }

    private static Material CreateSurface(Color color, double opacity, bool hasGloss)
    {
        var material = new MaterialGroup();
        var surfaceBrush = new SolidColorBrush(color)
        {
            Opacity = opacity
        };
        material.Children.Add(new DiffuseMaterial(surfaceBrush));

        if (hasGloss)
        {
            var glossBrush = new SolidColorBrush(Colors.White)
            {
                Opacity = opacity * 0.7
            };
            material.Children.Add(new SpecularMaterial(glossBrush, 72));
        }

        material.Freeze();
        return material;
    }

    private DrawingBrush CreateFlowBrush()
    {
        var drawing = new DrawingGroup();

        // 固定纹理范围，使水纹沿路径每隔一个场景单位重复一次。
        var textureBounds = new RectangleGeometry(new Rect(0, 0, 1, 1));
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, textureBounds));

        // 两条柔和的局部折光始终位于液芯内，不覆盖外侧管壁。
        AddWaterHighlight(drawing, new Rect(0.05, 0.08, 0.42, 0.3));
        AddWaterHighlight(drawing, new Rect(0.55, 0.56, 0.38, 0.25));
        drawing.Freeze();

        return new DrawingBrush(drawing)
        {
            Viewport = new Rect(0, 0, 1, 1),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 1, 1),
            ViewboxUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            Transform = _flowOffset
        };
    }

    private void AddWaterHighlight(DrawingGroup drawing, Rect bounds)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.25),
            EndPoint = new Point(1, 0.75)
        };
        gradient.GradientStops.Add(new GradientStop(CreateHighlightColor(0, 0), 0));
        gradient.GradientStops.Add(new GradientStop(CreateHighlightColor(75, 0), 0.25));
        gradient.GradientStops.Add(new GradientStop(CreateHighlightColor(245, 0.78), 0.62));
        gradient.GradientStops.Add(new GradientStop(CreateHighlightColor(0, 0), 1));
        drawing.Children.Add(new GeometryDrawing(gradient, null, new RectangleGeometry(bounds)));
    }

    private Color CreateHighlightColor(byte alpha, double whiteRatio)
    {
        Color color = FlowColor;
        return Color.FromArgb(
            alpha,
            (byte)(color.R + (255 - color.R) * whiteRatio),
            (byte)(color.G + (255 - color.G) * whiteRatio),
            (byte)(color.B + (255 - color.B) * whiteRatio));
    }

    private void UpdateVisibleLayers()
    {
        Model.Children.Clear();
        if (!_hasRoute)
        {
            return;
        }

        // 先画背面和内壁，再画水流、正面和端口，保留透明管壁的层次。
        Model.Children.Add(_backWall);
        Model.Children.Add(_innerWall);

        if (IsFilled && IsFlowing)
        {
            Model.Children.Add(_fluidCore);
        }

        Model.Children.Add(_frontWall);
        Model.Children.Add(_endRims);
    }

    private void UpdateAnimation()
    {
        bool isHostVisible = (bool)GetValue(IsHostVisibleProperty);
        bool shouldRun = _hasRoute && IsFilled && IsFlowing
            && IsAnimationEnabled && FlowSpeed > 0 && isHostVisible;

        bool stateChanged = shouldRun != _isAnimationRunning;
        bool speedChanged = shouldRun && _animationSpeed != FlowSpeed;
        if (stateChanged || speedChanged)
        {
            // 暂停或改速时保留当前进度，重新播放时从这里接着流动。
            double phase = (double)GetValue(PhaseProperty) % 1;
            BeginAnimation(PhaseProperty, null);
            SetValue(PhaseProperty, phase);

            _isAnimationRunning = shouldRun;
            _animationSpeed = FlowSpeed;

            if (shouldRun)
            {
                double durationSeconds = 1 / Math.Clamp(FlowSpeed, 0.001, 1000);
                var animation = new DoubleAnimation(
                    phase,
                    phase + 1,
                    TimeSpan.FromSeconds(durationSeconds))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                Timeline.SetDesiredFrameRate(animation, 30);
                BeginAnimation(PhaseProperty, animation);
            }
        }

        UpdateFlowOffset();
    }

    private void UpdateFlowOffset()
    {
        double phase = (double)GetValue(PhaseProperty);
        if (ReverseFlow)
        {
            _flowOffset.X = -phase;
        }
        else
        {
            _flowOffset.X = phase;
        }
    }

    /// <summary>
    /// 路径可能由长期存在的反馈源持有，用弱引用避免事件把已移除的控件一直留在内存里。
    /// 订阅和退订都在路径所属的 UI 线程进行。
    /// </summary>
    private sealed class PathChangeListener
    {
        private readonly WeakReference<FluidRouteVisual3D> _owner;
        private readonly Point3DCollection _path;

        public PathChangeListener(FluidRouteVisual3D owner, Point3DCollection path)
        {
            _owner = new WeakReference<FluidRouteVisual3D>(owner);
            _path = path;
            _path.Changed += OnChanged;
        }

        public void Detach()
        {
            // 冻结时 WPF 会清理 Changed 事件，此后不允许再修改事件订阅。
            if (!_path.IsFrozen)
            {
                _path.Changed -= OnChanged;
            }
        }

        private void OnChanged(object? sender, EventArgs args)
        {
            if (_owner.TryGetTarget(out FluidRouteVisual3D? pipe))
            {
                pipe.RebuildGeometry();
            }
            else
            {
                Detach();
            }
        }
    }
}
