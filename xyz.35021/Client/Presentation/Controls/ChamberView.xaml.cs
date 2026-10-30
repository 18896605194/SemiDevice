using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz._35021.Client.Presentation.Chambers;

namespace xyz._35021.Client.Presentation.Controls;

/// <summary>35021 正式腔体图。加载布局数据，全部动作取后台反馈；没有布局编辑或模拟指令。</summary>
public partial class ChamberView : UserControl
{
    private readonly OrthographicCamera _camera = new();
    private ChamberAssembly? _assembly;
    private string _roster = "";
    private double _yaw = -18, _pitch = 34, _zoom = 7.8;
    private Point? _drag;
    public ChamberView()
    {
        InitializeComponent(); Viewport.Camera = _camera;
        Viewport.SizeChanged += (_, _) => UpdateCamera();
        IsVisibleChanged += (_, _) => UpdateScene();
        Viewport.MouseLeftButtonDown += (_, args) => { _drag = args.GetPosition(Viewport); Viewport.CaptureMouse(); args.Handled = true; };
        Viewport.MouseLeftButtonUp += (_, args) => { _drag = null; Viewport.ReleaseMouseCapture(); args.Handled = true; };
        Viewport.LostMouseCapture += (_, _) => _drag = null;
        Viewport.MouseMove += (_, args) =>
        {
            if (_drag is not { } from) return;
            var point = args.GetPosition(Viewport); _yaw += (point.X - from.X) * 0.35; _pitch = Math.Clamp(_pitch - (point.Y - from.Y) * 0.25, 5, 89);
            _drag = point; UpdateCamera();
        };
        Viewport.MouseWheel += (_, args) => { _zoom = Math.Clamp(_zoom - args.Delta / 120d * 0.4, 4, 40); UpdateCamera(); args.Handled = true; };
    }
    public ChamberDeviceDataDto? Snapshot { get => (ChamberDeviceDataDto?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(nameof(Snapshot), typeof(ChamberDeviceDataDto), typeof(ChamberView), new PropertyMetadata(null, Changed));
    public ChamberLayoutModel? Layout { get => (ChamberLayoutModel?)GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(nameof(Layout), typeof(ChamberLayoutModel), typeof(ChamberView), new PropertyMetadata(null, Rebuild));
    public ChamberVisualCatalog? VisualCatalog { get => (ChamberVisualCatalog?)GetValue(VisualCatalogProperty); set => SetValue(VisualCatalogProperty, value); }
    public static readonly DependencyProperty VisualCatalogProperty = DependencyProperty.Register(nameof(VisualCatalog), typeof(ChamberVisualCatalog), typeof(ChamberView), new PropertyMetadata(null, Rebuild));
    public object? Wafer { get => GetValue(WaferProperty); set => SetValue(WaferProperty, value); }
    public static readonly DependencyProperty WaferProperty = DependencyProperty.Register(nameof(Wafer), typeof(object), typeof(ChamberView), new PropertyMetadata(null, Changed));
    public IReadOnlyDictionary<string, ChamberPartVisual> DisplayedParts => _assembly?.Visuals ?? new Dictionary<string, ChamberPartVisual>();
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((ChamberView)sender).UpdateScene();
    private static void Rebuild(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    { var control = (ChamberView)sender; control._roster = ""; control.UpdateScene(); }
    private void UpdateScene()
    {
        if (Snapshot is not { } state) return;
        var composed = ChamberLayoutComposer.Compose(state, Layout);
        string roster = state.Module + ":" + string.Join("|", composed.Parts.Select(p => p.Kind + ":" + p.Id + ":" + p.ParentId));
        if (_assembly is null || roster != _roster)
        {
            _assembly?.Dispose(); Viewport.Children.Clear();
            _assembly = new ChamberAssembly(composed, VisualCatalog ?? new ChamberVisualCatalog());
            Viewport.Children.Add(_assembly.World); _roster = roster;
        }
        _assembly.Apply(state, IsVisible, Wafer); UpdateCamera();
    }
    private void OnDefaultView(object sender, RoutedEventArgs args) { _yaw = -18; _pitch = 34; _zoom = 7.8; UpdateCamera(); }
    private void OnTopView(object sender, RoutedEventArgs args) { _yaw = 0; _pitch = 89; UpdateCamera(); }
    private void UpdateCamera()
    {
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(12 * Math.Cos(pitch) * Math.Sin(yaw), 12 * Math.Sin(pitch), 12 * Math.Cos(pitch) * Math.Cos(yaw));
        _camera.Position = new Point3D(0, 0.3, 0) + offset; _camera.LookDirection = -offset; _camera.UpDirection = new(0, 1, 0);
        _camera.Width = _zoom * Math.Max(1, Viewport.ActualHeight > 0 ? Viewport.ActualWidth / Viewport.ActualHeight / 1.6 : 1);
    }
}
