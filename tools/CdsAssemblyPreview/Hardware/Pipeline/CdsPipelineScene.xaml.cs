using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;

namespace CdsAssemblyPreview;

public partial class CdsPipelineScene : UserControl
{
    private readonly TankVisual3D _tank = new() { WallTransparency = 0.72 };
    private readonly List<FluidRouteVisual3D> _pipes = [];
    private readonly List<UIElement> _annotations = [];
    private CdsPipelinePreviewViewModel? _model;
    private string? _dragId;
    private Point _pressed;
    private Vector _offset;
    internal IReadOnlyList<FluidRouteVisual3D> Pipes => _pipes;

    public CdsPipelineScene()
    {
        InitializeComponent();
        var lights = new Model3DGroup(); lights.Children.Add(new AmbientLight(Color.FromRgb(140, 140, 140)));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -3, -2)));
        Viewport.Children.Add(new ModelVisual3D { Content = lights });
        for (int i = 0; i < 5; i++)
        {
            var pipe = new FluidRouteVisual3D { Transform = new ScaleTransform3D(100, 100, 100), WallOpacity = 0.55,
                WallColor = Color.FromRgb(130, 145, 160), FlowColor = Color.FromRgb(96, 188, 222), FlowSpeed = 0.65, BendRadius = 0.15 };
            _pipes.Add(pipe); Viewport.Children.Add(pipe);
        }
        Viewport.Children.Add(_tank);
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.SceneChanged -= Refresh;
            _model = DataContext as CdsPipelinePreviewViewModel;
            if (_model is not null) _model.SceneChanged += Refresh;
            Refresh();
        };
        Loaded += (_, _) => UpdateAnimation(); IsVisibleChanged += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => { foreach (var pipe in _pipes) pipe.IsAnimationEnabled = false; };
    }
    internal void Refresh()
    {
        if (_model is null || _model.Parts.Count != 6) return;
        var tank = _model.Part("TK1"); _tank.Transform = tank.Transform(); _tank.FillFraction = _model.FillPercent / 100;
        _tank.IsSelected = _model.Selected?.Id == tank.Id;
        Configure(PumpFront, _model.Part("P1"), _model.Pump); Configure(HeaterFront, _model.Part("H1"), _model.Heater);
        Place(InletSymbol, _model.Part("V1")); InletSymbol.IsOpen = _model.InletValve;
        Place(OutletSymbol, _model.Part("V2")); OutletSymbol.IsOpen = _model.OutletValve;
        var routes = _model.Routes();
        for (int i = 0; i < _pipes.Count; i++)
        {
            var pipe = _pipes[i];
            double radius = _model.PipeDiameter / 200;
            if (pipe.PipeRadius != radius) { pipe.Path = null; pipe.PipeRadius = radius; pipe.WallThickness = radius * 0.18; }
            var points = new Point3DCollection(routes[i].Select(p => new Point3D(p.X / 100, -p.Y / 100, 0)));
            if (pipe.Path is null || !pipe.Path.SequenceEqual(points)) { points.Freeze(); pipe.Path = points; }
            pipe.IsFilled = pipe.IsFlowing = _model.IsFlowing;
        }
        foreach (var annotation in _annotations) Overlay.Children.Remove(annotation); _annotations.Clear();
        foreach (var node in _model.Parts)
        {
            if (node.Kind == "Terminal")
                Annotate(new Rectangle { Width = node.Width, Height = node.Height, Stroke = Brush("DarkDisabledText"), StrokeThickness = 2 }, node.X, node.Y);
            var label = Label(node.Text, "Size22"); Annotate(label, node.Kind == "Terminal" ? node.X - 30 : node.X, node.Y - 50);
            if (node.Kind == "Pump" || node.Kind == "Heater")
            {
                var hardware = node.Kind == "Pump" ? _model.Pump : _model.Heater;
                var reading = Label(HardwareVisual.FormatReading(hardware.Reading, hardware.Unit, hardware.State) + " · " + hardware.StateText, "Size18",
                    hardware.State == HardwareState.Fault ? "DarkAlarmStatus" : "DarkPrimaryText");
                Annotate(reading, node.X, node.Y + node.Height + 22);
            }
            if (_model.Selected?.Id == node.Id)
            {
                Annotate(new Border { Width = node.Width + 18, Height = node.Height + 18, BorderBrush = Brush("DarkAccent"), BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(6), RenderTransform = new RotateTransform(node.Angle, node.Width / 2 + 9, node.Height / 2 + 9) }, node.X - 9, node.Y - 9);
                if (node.Kind is "Pump" or "Heater") foreach (bool outlet in new[] { false, true })
                {
                    var p = _model.Port(node.Id, outlet);
                    Annotate(new Ellipse { Width = 10, Height = 10, Fill = Brush("DarkSurfaceBackground"), Stroke = Brush("DarkAccent"), StrokeThickness = 2 }, p.X - 5, p.Y - 5);
                }
            }
        }
        foreach (var (text, value, offset) in new[] { ("HH", _model.FillPercent >= 90, 0), ("LL", _model.FillPercent >= 10, 32) })
            Annotate(new SignalLamp { Text = text, IsActive = value, IsAlarm = text == "HH" }, tank.X + tank.Width + 18, tank.Y + 22 + offset);
        UpdateAnimation();
    }
    private void Configure(ProcessHardwareScene scene, CdsNode node, HardwareDemo hardware)
    {
        Place(scene, node); scene.State = hardware.State; scene.Reading = hardware.Reading; scene.Target = hardware.Target;
        scene.ShowInternals = _model!.ShowInternals; scene.PowerFraction = _model.PowerFraction;
    }
    private static void Place(FrameworkElement visual, CdsNode node)
    {
        visual.Width = node.Width; visual.Height = node.Height; Canvas.SetLeft(visual, node.X); Canvas.SetTop(visual, node.Y);
        visual.RenderTransform = new RotateTransform(node.Angle, node.Width / 2, node.Height / 2);
    }
    private void UpdateAnimation() { foreach (var pipe in _pipes) pipe.IsAnimationEnabled = IsLoaded && IsVisible && _model?.IsFlowing == true; }
    private Brush Brush(string key) => (Brush)FindResource(key);
    private TextBlock Label(string text, string size, string color = "DarkPrimaryText") => new() { Text = text, Foreground = Brush(color), FontSize = (double)FindResource(size) };
    private void Annotate(UIElement element, double x, double y)
    { element.IsHitTestVisible = false; Canvas.SetLeft(element, x); Canvas.SetTop(element, y); Overlay.Children.Add(element); _annotations.Add(element); }

    internal bool PointerDown(Point point, int clicks = 1)
    {
        if (_model is null) return false;
        var node = _model.Parts.Reverse().FirstOrDefault(n => new RotateTransform(-n.Angle, n.X + n.Width / 2, n.Y + n.Height / 2)
            .Transform(point) is var p && new Rect(n.X - 6, n.Y - 6, n.Width + 12, n.Height + 12).Contains(p));
        if (node is null) return false;
        _model.Select(node.Id);
        if (clicks == 2 && node.Kind == "Valve") { _model.ValveCommand.Execute(_model.SelectedValve == true ? "close" : "open"); return true; }
        _dragId = node.Id; _pressed = point; _offset = point - new Point(node.X, node.Y); Stage.CaptureMouse(); return true;
    }
    internal void PointerMove(Point point)
    {
        if (_dragId is not null && (point - _pressed).Length >= 4) _model?.Move(_dragId, point - _offset);
    }
    internal void PointerUp() { _dragId = null; if (Stage.IsMouseCaptured) Stage.ReleaseMouseCapture(); }
    internal void FocusEditor() => Keyboard.Focus(Stage);
    private void OnDown(object sender, MouseButtonEventArgs e) { if (PointerDown(e.GetPosition(Stage), e.ClickCount)) { FocusEditor(); e.Handled = true; } }
    private void OnMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) PointerMove(e.GetPosition(Stage)); }
    private void OnUp(object sender, MouseButtonEventArgs e) => PointerUp();
    private void OnLostCapture(object sender, MouseEventArgs e) { if (!Stage.IsMouseCaptured) _dragId = null; }
}
