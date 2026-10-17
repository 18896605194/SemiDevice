using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 可直接放进 Grid、Canvas、StackPanel 的管路控件。
/// 默认是一根直管；设置 Points 后按二维路径绘制，相机和灯光由控件内部管理。
/// </summary>
public partial class FluidPipe : UserControl
{
    // 页面使用逻辑像素，内部三维模型每一个单位对应 100 个逻辑像素。
    private const double SceneScale = 0.01;
    private const double EdgeSpace = 2;
    private const double DefaultLength = 200;

    private PointsChangeListener? _pointsListener;
    private Rect _routeBounds = Rect.Empty;
    private bool _isReady;

    public FluidPipe()
    {
        InitializeComponent();
        _isReady = true;
        SetCurrentValue(PointsProperty, new PointCollection());
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateAppearance();
        UpdateFlow();
    }

    /// <summary>管中心的二维路径点，X 向右、Y 向下。不设置时按控件大小显示直管。</summary>
    public PointCollection? Points
    {
        get
        {
            return (PointCollection?)GetValue(PointsProperty);
        }
        set
        {
            SetValue(PointsProperty, value);
        }
    }

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(PointCollection),
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(null, OnPointsChanged));

    /// <summary>默认直管的方向。设置 Points 后按路径绘制。</summary>
    public Orientation Orientation
    {
        get
        {
            return (Orientation)GetValue(OrientationProperty);
        }
        set
        {
            SetValue(OrientationProperty, value);
        }
    }

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(Orientation.Horizontal, OnLayoutChanged),
        IsValidOrientation);

    /// <summary>管外直径，使用 WPF 逻辑像素。</summary>
    public double PipeThickness
    {
        get
        {
            return (double)GetValue(PipeThicknessProperty);
        }
        set
        {
            SetValue(PipeThicknessProperty, value);
        }
    }

    public static readonly DependencyProperty PipeThicknessProperty = DependencyProperty.Register(
        nameof(PipeThickness),
        typeof(double),
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(14d, OnLayoutChanged),
        IsPositiveFinite);

    /// <summary>管壁厚度，使用 WPF 逻辑像素。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(2d, OnLayoutChanged),
        IsPositiveFinite);

    /// <summary>转弯圆角半径，使用路径坐标单位。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(24d, OnLayoutChanged),
        IsNonNegativeFinite);

    /// <summary>液芯半径占内腔半径的比例，范围为 (0, 1]。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(0.58d, OnLayoutChanged),
        IsPositiveRatio);

    /// <summary>管壁不透明度强度，范围为 [0, 1]，越小越透明。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(0.18d, OnAppearanceChanged),
        IsOpacity);

    /// <summary>管壁颜色。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(Color.FromRgb(180, 180, 180), OnAppearanceChanged));

    /// <summary>液体底色，与 FlowColor 按 1:3 混合。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(Color.FromRgb(196, 196, 196), OnAppearanceChanged));

    /// <summary>水流主色和移动水纹的颜色。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(Color.FromRgb(82, 212, 239), OnAppearanceChanged));

    /// <summary>是否有液体。false 时不显示液芯。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(true, OnFlowChanged));

    /// <summary>是否流动。false 时清除液芯和水纹，只显示管壁。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(false, OnFlowChanged));

    /// <summary>水纹每秒移动的路径长度，0 时暂停；不是设备实测流量。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(55d, OnFlowChanged),
        IsNonNegativeFinite);

    /// <summary>是否沿路径反向流动。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(false, OnFlowChanged));

    /// <summary>动画开关。false 时冻结水纹，保留液芯和流动状态。</summary>
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
        typeof(FluidPipe),
        new FrameworkPropertyMetadata(true, OnFlowChanged));

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        double width;
        double height;
        double diameterWithSpace = PipeThickness + EdgeSpace * 2;

        if (Points == null || Points.Count == 0)
        {
            width = DefaultLength;
            height = Math.Max(24, diameterWithSpace);
            if (Orientation == Orientation.Vertical)
            {
                width = Math.Max(24, diameterWithSpace);
                height = DefaultLength;
            }
        }
        else
        {
            Rect bounds = GetPointsBounds();
            width = diameterWithSpace;
            height = diameterWithSpace;
            if (!bounds.IsEmpty)
            {
                width += bounds.Width;
                height += bounds.Height;
            }
        }

        return new Size(Math.Min(width, constraint.Width), Math.Min(height, constraint.Height));
    }

    private static void OnPointsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidPipe)sender;
        if (ReferenceEquals(args.OldValue, args.NewValue))
        {
            return;
        }

        pipe._pointsListener?.Detach();
        pipe._pointsListener = null;
        if (args.NewValue is PointCollection points && !points.IsFrozen)
        {
            pipe._pointsListener = new PointsChangeListener(pipe, points);
        }

        pipe.RefreshRoute();
    }

    private static void OnLayoutChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidPipe)sender;
        pipe.RefreshRoute();
    }

    private static void OnAppearanceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidPipe)sender;
        pipe.UpdateAppearance();
    }

    private static void OnFlowChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var pipe = (FluidPipe)sender;
        pipe.UpdateFlow();
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

    private static bool IsValidOrientation(object value)
    {
        var orientation = (Orientation)value;
        return orientation == Orientation.Horizontal || orientation == Orientation.Vertical;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        RebuildRoute();
        UpdateFlow();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        // 整个二维控件从页面卸下后，其内部 Viewport 仍存在，因此在这里明确停止动画。
        PipeModel.IsAnimationEnabled = false;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (Points == null || Points.Count == 0)
        {
            RebuildRoute();
        }
        else
        {
            UpdateCamera();
        }
    }

    private void RefreshRoute()
    {
        if (!_isReady)
        {
            return;
        }

        InvalidateMeasure();
        RebuildRoute();
    }

    private void RebuildRoute()
    {
        if (!_isReady)
        {
            return;
        }

        var path = new Point3DCollection();
        Rect bounds;

        if (Points == null || Points.Count == 0)
        {
            double length = PipeViewport.ActualWidth;
            if (Orientation == Orientation.Vertical)
            {
                length = PipeViewport.ActualHeight;
            }

            if (length <= 0)
            {
                length = DefaultLength;
            }

            // 直管随 Width / Height 伸长，但不随着长度增加而变粗。
            length = Math.Max(1, length - PipeThickness - EdgeSpace * 2);
            path.Add(new Point3D(0, 0, 0));
            if (Orientation == Orientation.Vertical)
            {
                path.Add(new Point3D(0, -length * SceneScale, 0));
                bounds = new Rect(0, 0, 0, length);
            }
            else
            {
                path.Add(new Point3D(length * SceneScale, 0, 0));
                bounds = new Rect(0, 0, length, 0);
            }
        }
        else
        {
            bounds = GetPointsBounds();
            if (!bounds.IsEmpty)
            {
                foreach (Point point in Points)
                {
                    // 页面坐标 Y 向下，三维坐标 Y 向上。
                    path.Add(new Point3D(point.X * SceneScale, -point.Y * SceneScale, 0));
                }
            }
        }

        _routeBounds = bounds;
        if (!_routeBounds.IsEmpty)
        {
            double margin = PipeThickness / 2 + EdgeSpace;
            _routeBounds.Inflate(margin, margin);
        }

        PipeModel.PipeRadius = PipeThickness * SceneScale / 2;
        PipeModel.WallThickness = WallThickness * SceneScale;
        PipeModel.BendRadius = BendRadius * SceneScale;
        PipeModel.FluidCoreRatio = FluidCoreRatio;

        path.Freeze();
        PipeModel.Path = path;
        UpdateCamera();
    }

    private Rect GetPointsBounds()
    {
        Rect bounds = Rect.Empty;
        if (Points == null)
        {
            return bounds;
        }

        foreach (Point point in Points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                return Rect.Empty;
            }

            bounds.Union(point);
        }

        return bounds;
    }

    private void UpdateCamera()
    {
        if (!_isReady || _routeBounds.IsEmpty)
        {
            return;
        }

        double width = PipeViewport.ActualWidth;
        double height = PipeViewport.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double centerX = _routeBounds.X + _routeBounds.Width / 2;
        double centerY = _routeBounds.Y + _routeBounds.Height / 2;
        double aspectRatio = width / height;
        double visibleWidth = Math.Max(_routeBounds.Width, _routeBounds.Height * aspectRatio);

        // 正视图等比取景，自动留出管壁和弯头的边缘，不要求页面设置相机。
        double cameraDistance = Math.Max(10, PipeThickness * SceneScale * 2);
        PipeCamera.Position = new Point3D(centerX * SceneScale, -centerY * SceneScale, cameraDistance);
        PipeCamera.FarPlaneDistance = cameraDistance * 3;
        PipeCamera.Width = Math.Max(0.001, visibleWidth * SceneScale);
    }

    private void UpdateAppearance()
    {
        if (!_isReady)
        {
            return;
        }

        PipeModel.WallOpacity = WallOpacity;
        PipeModel.WallColor = WallColor;
        PipeModel.FluidColor = FluidColor;
        PipeModel.FlowColor = FlowColor;
    }

    private void UpdateFlow()
    {
        if (!_isReady)
        {
            return;
        }

        PipeModel.IsFilled = IsFilled;
        PipeModel.IsFlowing = IsFlowing;
        PipeModel.FlowSpeed = FlowSpeed * SceneScale;
        PipeModel.ReverseFlow = ReverseFlow;
        PipeModel.IsAnimationEnabled = IsLoaded && IsAnimationEnabled;
    }

    private sealed class PointsChangeListener
    {
        private readonly WeakReference<FluidPipe> _owner;
        private readonly PointCollection _points;

        public PointsChangeListener(FluidPipe owner, PointCollection points)
        {
            _owner = new WeakReference<FluidPipe>(owner);
            _points = points;
            _points.Changed += OnChanged;
        }

        public void Detach()
        {
            if (!_points.IsFrozen)
            {
                _points.Changed -= OnChanged;
            }
        }

        private void OnChanged(object? sender, EventArgs args)
        {
            if (_owner.TryGetTarget(out FluidPipe? pipe))
            {
                pipe.RefreshRoute();
            }
            else
            {
                Detach();
            }
        }
    }
}
