using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;

namespace xyz._35021.Client.Manual.Views;

/// <summary>仅加载 XAML 并处理视角交互；状态通过依赖属性绑定，不计算设备运动或安装位置。</summary>
public partial class ChamberDiagram : UserControl
{
    private ChamberDiagramContent? _content;
    private IReadOnlyDictionary<string, ModelVisual3D> _parts = new Dictionary<string, ModelVisual3D>();
    private double _yaw = -18, _pitch = 34, _zoom = 7.8;
    private Point? _drag;

    public ChamberDiagram()
    {
        InitializeComponent();
    }
    private void ConfigureInteraction(Viewport3D viewport)
    {
        viewport.MouseLeftButtonDown += (_, args) => { _drag = args.GetPosition(viewport); viewport.CaptureMouse(); args.Handled = true; };
        viewport.MouseLeftButtonUp += (_, args) => { _drag = null; viewport.ReleaseMouseCapture(); args.Handled = true; };
        viewport.LostMouseCapture += (_, _) => _drag = null;
        viewport.MouseMove += (_, args) =>
        {
            if (_drag is not { } from) return;
            var point = args.GetPosition(viewport); _yaw += (point.X - from.X) * 0.35; _pitch = Math.Clamp(_pitch - (point.Y - from.Y) * 0.25, 5, 89);
            _drag = point; UpdateCamera();
        };
        viewport.MouseWheel += (_, args) => { _zoom = Math.Clamp(_zoom - args.Delta / 120d * 0.4, 4, 40); UpdateCamera(); args.Handled = true; };
    }
    public string DiagramXaml { get => (string)GetValue(DiagramXamlProperty); set => SetValue(DiagramXamlProperty, value); }
    public static readonly DependencyProperty DiagramXamlProperty = DependencyProperty.Register(nameof(DiagramXaml), typeof(string), typeof(ChamberDiagram),
        new PropertyMetadata("", (d, _) => ((ChamberDiagram)d).LoadDiagram()));
    public IReadOnlyDictionary<string, ModelVisual3D> DisplayedParts => _parts;
    public Viewport3D? Viewport => _content?.Viewport;

    private void LoadDiagram()
    {
        if (DiagramXaml.Length == 0) return;
        try
        {
            var replacement = ChamberDiagramXaml.Load(DiagramXaml);
            _content = replacement; _parts = replacement.Parts.ToDictionary(p => p.Key, p => p.Value.Visual!);
            DiagramHost.Content = replacement.View; ConfigureInteraction(replacement.Viewport);
            UpdateCamera(); DiagramError.Text = "";
        }
        catch (Exception failure) { DiagramError.Text = L10n.Get("m35021.chamber.layout_failed", failure.Message); }
    }
    private void OnDefaultView(object sender, RoutedEventArgs args) { _yaw = -18; _pitch = 34; _zoom = 7.8; UpdateCamera(); }
    private void OnTopView(object sender, RoutedEventArgs args) { _yaw = 0; _pitch = 89; UpdateCamera(); }
    private void UpdateCamera()
    {
        if (_content is null) return;
        var camera = _content.Camera;
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(12 * Math.Cos(pitch) * Math.Sin(yaw), 12 * Math.Sin(pitch), 12 * Math.Cos(pitch) * Math.Cos(yaw));
        camera.Position = new Point3D(0, 0.3, 0) + offset; camera.LookDirection = -offset; camera.UpDirection = new(0, 1, 0);
        camera.Width = _zoom;
    }
}
