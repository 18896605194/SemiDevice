using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Localization;

namespace CdsAssemblyPreview;

public partial class CdsScene : UserControl
{
    private readonly Dictionary<string, TankVisual3D> _tanks = [];
    private readonly Dictionary<string, FluidRouteVisual3D> _pipes = [];
    private readonly Dictionary<string, CdsPipe> _appearances = [];
    private readonly Dictionary<string, ValveSymbol> _valves = [];
    private readonly Dictionary<string, FrameworkElement> _hardware = [];
    private PreviewViewModel? _model;
    private string? _dragId;
    private Vector _dragOffset;
    public IReadOnlyDictionary<string, FluidRouteVisual3D> Pipes => _pipes;
    public IReadOnlyDictionary<string, TankVisual3D> Tanks => _tanks;
    internal IReadOnlyDictionary<string, ValveSymbol> Valves => _valves;
    internal IReadOnlyDictionary<string, FrameworkElement> Hardware => _hardware;
    public CdsScene()
    {
        InitializeZoomCommands();
        InitializeComponent();
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(140, 140, 140)));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -3, -2)));
        Viewport.Children.Add(new ModelVisual3D { Content = lights });
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.SceneChanged -= Refresh;
            _model = DataContext as PreviewViewModel;
            if (_model is not null) _model.SceneChanged += Refresh;
            Refresh();
        };
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    public void Refresh()
    {
        if (_model is null || _model.Layout.Width < 1) return;
        var layout = _model.Layout;
        Stage.Width = layout.Width; Stage.Height = layout.Height;
        UpdateFit();
        Camera.Position = new Point3D(layout.Width / 2, -layout.Height / 2, 2000); Camera.Width = layout.Width;
        foreach (string id in _tanks.Keys.Except(layout.Nodes.Where(n => n.Kind == "Tank").Select(n => n.Id)).ToArray())
        { Viewport.Children.Remove(_tanks[id]); _tanks.Remove(id); }
        foreach (var node in layout.Nodes.Where(n => n.Kind == "Tank"))
        {
            if (!_tanks.TryGetValue(node.Id, out var body))
            {
                body = new TankVisual3D { WallTransparency = 0.75 }; _tanks.Add(node.Id, body); Viewport.Children.Add(body);
            }
            body.Transform = node.Transform();
            body.WallTransparency = node.WallTransparency;
            var tank = _model.Snapshot.Tanks.Single(t => CdsLayoutComposer.Relative(_model.Snapshot.ComponentRoot, t.Data.Path) == CdsLayoutComposer.Relative(_model.Snapshot.ComponentRoot, node.Component));
            double[] fractions = [0, 0.05, 0.205, 0.47, 0.72, 0.865, 0.998];
            body.FillFraction = fractions[Math.Clamp(tank.Data.Level, 0, 6)]; body.IsAlarm = tank.Data.Level >= 5;
            body.IsSelected = !_model.IsSimulationMode && _model.Selected?.Id == node.Id;
        }
        foreach (string id in _pipes.Keys.Except(layout.Pipes.Select(p => p.Id)).ToArray())
        { _pipes[id].IsAnimationEnabled = false; Viewport.Children.Remove(_pipes[id]); _pipes.Remove(id); _appearances.Remove(id); }
        foreach (var pipe in layout.Pipes)
        {
            if (!_pipes.TryGetValue(pipe.Id, out var visual))
            {
                visual = new FluidRouteVisual3D { Transform = new ScaleTransform3D(100, 100, 100),
                    PipeRadius = pipe.Diameter / 2 * 0.01, WallThickness = pipe.WallThickness * 0.01, BendRadius = pipe.BendRadius * 0.01,
                    WallOpacity = pipe.WallOpacity, WallColor = pipe.WallColor, FlowColor = pipe.FlowColor,
                    FlowSpeed = pipe.FlowSpeed * 0.01, ReverseFlow = pipe.ReverseFlow, IsFilled = false };
                _pipes.Add(pipe.Id, visual); Viewport.Children.Insert(1, visual);
            }
            if (!_appearances.TryGetValue(pipe.Id, out var previous) || previous != pipe)
            {
                visual.Path = null;
                visual.PipeRadius = pipe.Diameter / 2 * 0.01; visual.WallThickness = pipe.WallThickness * 0.01; visual.BendRadius = pipe.BendRadius * 0.01;
                visual.WallOpacity = pipe.WallOpacity; visual.WallColor = pipe.WallColor; visual.FlowColor = pipe.FlowColor;
                visual.FlowSpeed = pipe.FlowSpeed * 0.01; visual.ReverseFlow = pipe.ReverseFlow; _appearances[pipe.Id] = pipe;
                visual.FluidCoreRatio = pipe.FluidCoreRatio;
            }
            Point3D Scale(Point3D p) => new(p.X * 0.01, p.Y * 0.01, p.Z * 0.01);
            var points = new Point3DCollection { Scale(layout.Port(pipe.From)) };
            foreach (var p in pipe.Bends) points.Add(Scale(new Point3D(p.X, -p.Y, 0)));
            points.Add(Scale(layout.Port(pipe.To)));
            if (visual.Path is null || !visual.Path.SequenceEqual(points)) { points.Freeze(); visual.Path = points; }
            var valves = new[] { pipe.From, pipe.To }.Select(CdsLayoutComposer.Owner).Distinct()
                .Select(id => layout.Nodes.Single(n => n.Id == id)).Where(n => n.Kind == "Valve").ToArray();
            bool flowing = valves.Length > 0 && valves.All(n => _model.ValveStates.GetValueOrDefault(_model.Snapshot.ComponentRoot + "." + n.Component) == true);
            visual.IsFlowing = flowing; visual.IsFilled = pipe.IsFilled && flowing;
            visual.IsSelected = !_model.IsSimulationMode && _model.SelectedPipeId == pipe.Id;
        }
        DrawOverlay(); UpdateAnimation();
    }

    private void DrawOverlay()
    {
        if (_model is null) return;
        Overlay.Children.Clear();
        var layout = _model.Layout;
        bool showEditing = !_model.IsSimulationMode;
        if (showEditing && _model.VerticalGuideX is { } guide)
            Overlay.Children.Add(new Line { X1 = guide, X2 = guide, Y1 = 0, Y2 = layout.Height,
                Stroke = Brush("DarkAccent"), StrokeThickness = 1, StrokeDashArray = [5, 4], Opacity = 0.7, IsHitTestVisible = false });
        foreach (string id in _valves.Keys.Except(layout.Nodes.Where(n => n.Kind == "Valve").Select(n => n.Id)).ToArray()) _valves.Remove(id);
        foreach (string id in _hardware.Keys.Except(layout.Nodes.Where(n => n.Kind is "Hardware" or "Analog").Select(n => n.Id)).ToArray()) _hardware.Remove(id);
        if (showEditing && _model.SelectedPipe is { } selectedPipe)
        {
            var start = layout.Port(selectedPipe.From); var end = layout.Port(selectedPipe.To);
            Overlay.Children.Add(new Polyline { Points = new PointCollection(new[] { new Point(start.X, -start.Y) }.Concat(selectedPipe.Bends).Append(new Point(end.X, -end.Y))),
                Stroke = Brush("DarkAccent"), StrokeThickness = 2, StrokeDashArray = [4, 3], IsHitTestVisible = false });
            foreach (var bend in selectedPipe.Bends)
                Add(new Rectangle { Width = 12, Height = 12, Fill = Brush("DarkSurfaceBackground"), Stroke = Brush("DarkAccent"), StrokeThickness = 2 }, bend.X - 6, bend.Y - 6);
        }
        foreach (var n in layout.Nodes)
        {
            if (n.Kind == "Tank")
            {
                var tank = _model.Snapshot.Tanks.Single(t => CdsLayoutComposer.Relative(_model.Snapshot.ComponentRoot, t.Data.Path) == CdsLayoutComposer.Relative(_model.Snapshot.ComponentRoot, n.Component));
                Add(Label(n.Text, "Size22"), n.X, n.Y - 74);
                var hit = new Border { Width = n.Width, Height = n.Height, Background = Brushes.Transparent, Tag = n.Id, Cursor = Cursors.Hand };
                hit.RenderTransform = new RotateTransform(n.Angle, n.Width / 2, n.Height / 2);
                if (showEditing && _model.Selected?.Id == n.Id) { hit.BorderBrush = Brush("DarkAccent"); hit.BorderThickness = new Thickness(1); hit.CornerRadius = new CornerRadius(12); }
                Add(hit, n.X, n.Y);
                Add(Label(L10n.Get("cdsp.level." + tank.Data.Level), "Size14", "DarkDisabledText"), n.X + 25, n.Y + n.Height + 18);
            }
            else if (n.Kind == "Sensor")
            {
                var hit = new Border { Width = n.Width, Height = n.Height, Background = Brushes.Transparent, Tag = n.Id, Cursor = Cursors.SizeAll };
                if (showEditing && _model.Selected?.Id == n.Id) { hit.BorderBrush = Brush("DarkAccent"); hit.BorderThickness = new Thickness(1); }
                hit.RenderTransform = new RotateTransform(n.Angle, n.Width / 2, n.Height / 2); Add(hit, n.X, n.Y);
                var lamp = new Viewbox { Width = n.Width, Height = n.Height, Stretch = Stretch.Uniform, IsHitTestVisible = false,
                    Child = new SignalLamp { Text = n.Text, IsActive = _model.FindSensor(n.Component)?.IsActive, IsAlarm = n.SensorAlarm },
                    RenderTransform = new RotateTransform(n.Angle, n.Width / 2, n.Height / 2) };
                Add(lamp, n.X, n.Y);
            }
            else if (n.Kind == "Valve")
            {
                string path = _model.Snapshot.ComponentRoot + "." + n.Component;
                var hit = new Border { Width = n.Width + 32, Height = n.Height + 60, Background = Brushes.Transparent,
                    Tag = n.Id, Cursor = Cursors.SizeAll, CornerRadius = new CornerRadius(5) };
                if (showEditing && _model.Selected?.Id == n.Id) { hit.BorderBrush = Brush("DarkAccent"); hit.BorderThickness = new Thickness(1.5); }
                Add(hit, n.X - 16, n.Y - 31);
                if (!_valves.TryGetValue(n.Id, out var symbol)) { symbol = new ValveSymbol(); _valves.Add(n.Id, symbol); }
                symbol.Width = n.Width; symbol.Height = n.Height; symbol.Tag = n.Id; symbol.ToolTip = path; symbol.Cursor = Cursors.SizeAll;
                symbol.RenderTransform = new RotateTransform(n.Angle, n.Width / 2, n.Height / 2);
                Add(symbol, n.X, n.Y);
                symbol.IsOpen = _model.ValveStates.GetValueOrDefault(path);
                string state = L10n.Get("cdsp.short." + (symbol.IsOpen switch { true => "open", false => "closed", _ => "unknown" }));
                Add(Label(n.Text + " · " + state, "Size12", symbol.IsOpen == true ? "DarkSignalLampOn" : "DarkDisabledText"), n.X - 12, n.Y + n.Height + 6);
            }
            else if (n.Kind is "Hardware" or "Analog")
            {
                if (!_model.HardwareStates.TryGetValue(_model.Snapshot.ComponentRoot + "." + n.Component, out var state)) continue;
                bool front = n.VisualKind is FluidHardwareKind.CentrifugalPump or FluidHardwareKind.InlineHeater;
                if (!_hardware.TryGetValue(n.Id, out var visual) || front != (visual is ProcessHardwareScene))
                {
                    visual = n.VisualKind is FluidHardwareKind.CentrifugalPump or FluidHardwareKind.InlineHeater
                        ? new ProcessHardwareScene { Kind = n.VisualKind.Value, ViewMode = HardwareViewMode.Front2D, IsHitTestVisible = false }
                        : n.Kind == "Analog" ? new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, IsHitTestVisible = false }
                        : new HardwareVisual { Kind = n.VisualKind!.Value, IsHitTestVisible = false };
                    _hardware[n.Id] = visual;
                }
                visual.Width = n.Width; visual.Height = n.Height; visual.RenderTransform = new RotateTransform(n.Angle, n.Width / 2, n.Height / 2);
                if (visual is ProcessHardwareScene scene) { scene.Kind = n.VisualKind!.Value; scene.State = state.State; scene.Reading = state.Reading; scene.Target = state.Target; scene.PowerFraction = state.PowerFraction; }
                if (visual is HardwareVisual symbol) { symbol.Kind = n.VisualKind!.Value; symbol.State = state.State; symbol.Reading = state.Reading; symbol.Target = state.Target; symbol.Unit = state.Unit; }
                if (visual is TextBlock reading) { reading.Text = n.Text + " · " + HardwareVisual.FormatReading(state.Reading, state.Unit, state.State); reading.Foreground = Brush("DarkPrimaryText"); reading.FontSize = (double)FindResource("Size14"); }
                if (showEditing && _model.Selected?.Id == n.Id) Add(new Border { Width = n.Width, Height = n.Height, BorderBrush = Brush("DarkAccent"), BorderThickness = new Thickness(1.5),
                    RenderTransform = new RotateTransform(n.Angle, n.Width / 2, n.Height / 2) }, n.X, n.Y);
                Add(visual, n.X, n.Y);
                if (n.Kind == "Hardware") { Add(Label(n.Text, "Size16"), n.X, n.Y - 28); Add(Label(HardwareVisual.FormatReading(state.Reading, state.Unit, state.State), "Size14"), n.X, n.Y + n.Height + 10); }
            }
            else
            {
                Add(new Rectangle { Width = n.Width, Height = n.Height, Stroke = Brush(showEditing && _model.Selected?.Id == n.Id ? "DarkAccent" : "DarkDisabledText"), StrokeThickness = 2,
                    Fill = Brush("DarkSurfaceBackground"), Tag = n.Id, Cursor = Cursors.SizeAll,
                    RenderTransform = new RotateTransform(n.Angle, n.Width / 2, n.Height / 2) }, n.X - n.Width / 2, n.Y - n.Height / 2);
                Add(Label(n.Text, "Size12", "DarkDisabledText"), n.X - 18, n.Y + 14);
            }
        }
        if (!showEditing || !_model.ConnectMode) return;
        foreach (string port in layout.Ports)
        {
            var p = layout.Port(port);
            var dot = new Ellipse { Width = 14, Height = 14, Fill = Brush(_model.StartPort == port ? "DarkAccent" : "DarkSurfaceBackground"),
                Stroke = Brush("DarkAccent"), StrokeThickness = 2, ToolTip = port, Cursor = Cursors.Cross };
            Add(dot, p.X - 7, -p.Y - 7);
        }
    }

    private TextBlock Label(string text, string size, string color = "DarkPrimaryText") => new()
    { Text = text, FontSize = (double)FindResource(size), Foreground = Brush(color), IsHitTestVisible = false };
    private Brush Brush(string key) => (Brush)FindResource(key);
    private void Add(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); Overlay.Children.Add(element); }
    private void UpdateAnimation()
    {
        foreach (var pair in _pipes) pair.Value.IsAnimationEnabled = IsVisible && (_model?.Layout.Pipes.SingleOrDefault(p => p.Id == pair.Key)?.IsAnimationEnabled ?? false);
    }
}
