using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// Wafer/Disk 的三维版本。复用独立 Wafer 实例的盘面、状态色及右键交互，原二维控件不变。
/// 局部 Y 向上，底面中心为原点。旋转速度沿用二维控件的度/秒语义（大于 1 时旋转）。
/// </summary>
public sealed class DiskVisual3D : HardwareVisual3D
{
    private readonly Wafer _face = new() { Width = 256, Height = 256 };
    private readonly Viewport2DVisual3D _surface = new();
    private readonly AxisAngleRotation3D _rotation = new(new Vector3D(0, -1, 0), 0);

    public DiskVisual3D()
    {
        _face.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/xyz.Client.Presentation;component/Styles/DiskVisual3DStyles.xaml", UriKind.Relative)
        });
        _face.SetResourceReference(Control.TemplateProperty, "DiskVisual3DFaceTemplate");
        // 停用此私有二维实例的旋转时钟；实际旋转交给整个三维盘。
        ((RotateTransform)_face.FindName("rotateTransform")).BeginAnimation(RotateTransform.AngleProperty, null);
        var rotation = new RotateTransform3D(_rotation);
        Model.Transform = rotation;
        _surface.Transform = rotation;
        var material = new DiffuseMaterial(Brushes.White);
        Viewport2DVisual3D.SetIsVisualHostMaterial(material, true);
        _surface.Material = material;
        _surface.Visual = _face;
        Children.Add(_surface);
        foreach (var (target, path) in new (DependencyProperty, string)[]
        {
            (Wafer.DataProperty, nameof(Data)), (Wafer.CreateCommandProperty, nameof(CreateCommand)),
            (Wafer.DeleteCommandProperty, nameof(DeleteCommand)), (Wafer.CommandParameterProperty, nameof(CommandParameter)),
            (Wafer.CreateEnableProperty, nameof(CreateEnable)), (Wafer.DeleteEnableProperty, nameof(DeleteEnable)),
            (Wafer.IsDiskVisibleProperty, nameof(IsDiskVisible)), (Wafer.FillColorProperty, nameof(FillColor)),
            (Control.BorderBrushProperty, nameof(BorderBrush)), (Control.BorderThicknessProperty, nameof(BorderThickness)),
            (Wafer.LabelProperty, nameof(Label))
        })
        {
            BindingOperations.SetBinding(_face, target, new Binding(path) { Source = this, Mode = BindingMode.OneWay });
        }

        RebuildGeometry();
        UpdateLabel();
    }

    public object? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(object), typeof(DiskVisual3D), new PropertyMetadata(null, OnDisplayChanged));
    public ICommand? CreateCommand { get => (ICommand?)GetValue(CreateCommandProperty); set => SetValue(CreateCommandProperty, value); }
    public static readonly DependencyProperty CreateCommandProperty = DependencyProperty.Register(
        nameof(CreateCommand), typeof(ICommand), typeof(DiskVisual3D), new PropertyMetadata(null, OnDisplayChanged));
    public ICommand? DeleteCommand { get => (ICommand?)GetValue(DeleteCommandProperty); set => SetValue(DeleteCommandProperty, value); }
    public static readonly DependencyProperty DeleteCommandProperty = DependencyProperty.Register(
        nameof(DeleteCommand), typeof(ICommand), typeof(DiskVisual3D), new PropertyMetadata(null));
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(DiskVisual3D), new PropertyMetadata(null));
    public bool CreateEnable { get => (bool)GetValue(CreateEnableProperty); set => SetValue(CreateEnableProperty, value); }
    public static readonly DependencyProperty CreateEnableProperty = DependencyProperty.Register(
        nameof(CreateEnable), typeof(bool), typeof(DiskVisual3D), new PropertyMetadata(true));
    public bool DeleteEnable { get => (bool)GetValue(DeleteEnableProperty); set => SetValue(DeleteEnableProperty, value); }
    public static readonly DependencyProperty DeleteEnableProperty = DependencyProperty.Register(
        nameof(DeleteEnable), typeof(bool), typeof(DiskVisual3D), new PropertyMetadata(true));
    public bool IsDiskVisible { get => (bool)GetValue(IsDiskVisibleProperty); set => SetValue(IsDiskVisibleProperty, value); }
    public static readonly DependencyProperty IsDiskVisibleProperty = DependencyProperty.Register(
        nameof(IsDiskVisible), typeof(bool), typeof(DiskVisual3D), new PropertyMetadata(false, OnDisplayChanged));
    public Brush FillColor { get => (Brush)GetValue(FillColorProperty); set => SetValue(FillColorProperty, value); }
    public static readonly DependencyProperty FillColorProperty = DependencyProperty.Register(
        nameof(FillColor), typeof(Brush), typeof(DiskVisual3D), new PropertyMetadata(Brushes.Gray));
    public Brush BorderBrush { get => (Brush)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(DiskVisual3D), new PropertyMetadata(Brushes.Gray));
    public Thickness BorderThickness { get => (Thickness)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(Thickness), typeof(DiskVisual3D), new PropertyMetadata(new Thickness(1)));
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(DiskVisual3D), new PropertyMetadata(string.Empty, OnLabelChanged));
    public double RotationSpeed { get => (double)GetValue(RotationSpeedProperty); set => SetValue(RotationSpeedProperty, value); }
    public static readonly DependencyProperty RotationSpeedProperty = DependencyProperty.Register(
        nameof(RotationSpeed), typeof(double), typeof(DiskVisual3D), new PropertyMetadata(0d, OnRotationChanged),
        value => value is double speed && double.IsFinite(speed) && speed >= 0);
    public bool RotateClockwise { get => (bool)GetValue(RotateClockwiseProperty); set => SetValue(RotateClockwiseProperty, value); }
    public static readonly DependencyProperty RotateClockwiseProperty = DependencyProperty.Register(
        nameof(RotateClockwise), typeof(bool), typeof(DiskVisual3D), new PropertyMetadata(true, OnRotationChanged));
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(DiskVisual3D), new PropertyMetadata(1.32d, OnGeometryChanged), IsPositiveFinite);
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(DiskVisual3D), new PropertyMetadata(0.045d, OnGeometryChanged), IsPositiveFinite);

    /// <summary>
    /// 盘下面主轴（旋转电机）露出来的长度：盘架高了就用一根轴撑到安装面上，免得悬空；0 = 不画主轴（默认）。
    /// </summary>
    public double SpindleHeight { get => (double)GetValue(SpindleHeightProperty); set => SetValue(SpindleHeightProperty, value); }
    public static readonly DependencyProperty SpindleHeightProperty = DependencyProperty.Register(
        nameof(SpindleHeight), typeof(double), typeof(DiskVisual3D), new PropertyMetadata(0d, OnGeometryChanged),
        value => value is double height && double.IsFinite(height) && height >= 0);

    /// <summary>主轴半径占盘半径的比例。</summary>
    private const double SpindleRadiusRatio = 0.12;

    private static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        "Angle", typeof(double), typeof(DiskVisual3D), new PropertyMetadata(0d, OnAngleChanged));
    private static readonly DependencyProperty IsHostVisibleProperty = DependencyProperty.Register(
        "IsHostVisible", typeof(bool), typeof(DiskVisual3D), new PropertyMetadata(false, OnRotationChanged));
    private static bool IsPositiveFinite(object value) => value is double v && double.IsFinite(v) && v > 0;
    private static void OnDisplayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var disk = (DiskVisual3D)sender;
        disk.UpdateDisplay();
        disk.UpdateRotation();
    }
    private static void OnLabelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DiskVisual3D)sender).UpdateLabel();
    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DiskVisual3D)sender).RebuildGeometry();
    private static void OnRotationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DiskVisual3D)sender).UpdateRotation();
    private static void OnAngleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DiskVisual3D)sender)._rotation.Angle = (double)args.NewValue;

    protected override void OnSceneConnectionChanged()
    {
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

    private void UpdateRotation()
    {
        // 先记住当前角度，再移除时钟，停止、换速和反转均不跳回初始位置。
        double current = (double)GetValue(AngleProperty) % 360;
        SetValue(AngleProperty, current);
        BeginAnimation(AngleProperty, null);
        bool spinning = RotationSpeed > 1 && (Data is not null || IsDiskVisible);
        SetVisualActive(spinning);
        if (!spinning || !(bool)GetValue(IsHostVisibleProperty))
        {
            return;
        }

        var animation = new DoubleAnimation(current, current + (RotateClockwise ? 360 : -360),
            TimeSpan.FromSeconds(Math.Clamp(360 / RotationSpeed, 0.001, 360))) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(animation, 30);
        BeginAnimation(AngleProperty, animation);
    }

    private void UpdateDisplay()
    {
        Content = Data is not null || IsDiskVisible ? Model : null;
        // 保留二维控件的空片建片命中区域；没有数据/常显/建片命令时完全隐藏。
        _surface.Visual = Data is not null || IsDiskVisible || CreateCommand is not null ? _face : null;
    }

    private void UpdateLabel()
    {
        // 只调整本控件私有实例，二维 Wafer 的 XAML 与其他实例保持不变。
        var root = (Grid)_face.FindName("rootGrid");
        var text = root.Children.OfType<Viewbox>().Select(v => v.Child).OfType<TextBlock>().Single();
        if (string.IsNullOrEmpty(Label))
        {
            BindingOperations.SetBinding(text, TextBlock.TextProperty, new Binding("Data.LpSlot") { Source = this });
        }
        else
        {
            text.SetValue(TextBlock.TextProperty, Label);
        }
    }

    private void RebuildGeometry()
    {
        ClearMeshes();
        AddMesh(HardwareMesh3D.Cylinder(Radius, Thickness, 0), 0.8);
        if (SpindleHeight > 0)
        {
            AddMesh(HardwareMesh3D.Cylinder(Radius * SpindleRadiusRatio, SpindleHeight, -SpindleHeight), 0.7);
        }

        AddMesh(HardwareMesh3D.Annulus(Radius * 1.005, Radius * 1.025), 1.3)
            .Transform = new TranslateTransform3D(0, Thickness, 0);
        // 边缘方向标记让没有文字的盘面也能看出旋转。
        AddMesh(HardwareMesh3D.ChamferedBox(new Point3D(Radius * 0.86, Thickness + 0.003, -Radius * 0.025),
            Radius * 0.1, 0.006, Radius * 0.05), 1.4);
        // Wafer 内部 100×100 画布有 2 单位留白，使可见圆盘半径与实体侧壁对齐。
        double r = Radius / 0.96;
        double y = Thickness + 0.001;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection { new(-r, y, -r), new(-r, y, r), new(r, y, r), new(r, y, -r) },
            TextureCoordinates = new PointCollection { new(0, 0), new(0, 1), new(1, 1), new(1, 0) },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };
        mesh.Freeze();
        _surface.Geometry = mesh;
        UpdateDisplay();
    }
}
