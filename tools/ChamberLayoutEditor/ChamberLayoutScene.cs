using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using xyz.Client.Presentation.Controls.ThreeD;

namespace ChamberLayoutEditor;

/// <summary>编辑、预览共用原生三维组件；编辑安装位置和模拟反馈完全分开。</summary>
public sealed class ChamberLayoutScene : Grid
{
    private readonly Viewport3D _viewport = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly OrthographicCamera _camera = new();
    private readonly Dictionary<DependencyObject, ChamberPlacement> _owners = [];
    private readonly Dictionary<string, PartVisual> _parts = [];
    private readonly Dictionary<ChamberPlacement, Rect> _labels = [];
    private readonly ModelVisual3D _world = new();
    private ChamberEditorViewModel? _model;
    private ChamberBaseVisual3D? _base;
    private Point? _drag;
    private Point? _orbit;
    private double _startX, _startZ;
    private double _yaw = -18, _pitch = 34, _width = 10.8;
    private bool _queued;

    public ChamberLayoutScene()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        _viewport.Camera = _camera;
        _viewport.Children.Add(_world);
        Children.Add(_viewport);
        Children.Add(_overlay);
        PreviewMouseLeftButtonDown += OnPointerDown;
        PreviewMouseLeftButtonUp += OnPointerUp;
        PreviewMouseRightButtonDown += OnOrbitDown;
        PreviewMouseRightButtonUp += (_, e) => { _orbit = null; ReleaseMouseCapture(); e.Handled = true; };
        PreviewMouseMove += OnPointerMove;
        PreviewMouseWheel += (_, e) => { _width = Math.Clamp(_width - e.Delta / 120d * 0.5, 5, 24); UpdateCamera(); e.Handled = true; };
        LostMouseCapture += (_, _) => { if (_drag is not null) { _drag = null; _model?.CancelDrag(); Apply(); } _orbit = null; };
        SizeChanged += (_, _) => UpdateCamera();
        IsVisibleChanged += (_, _) => Apply();
    }

    public void Bind(ChamberEditorViewModel model)
    {
        _model = model;
        model.DocumentChanged += () => { _drag = null; _orbit = null; ReleaseMouseCapture(); Rebuild(); };
        model.PlacementChanged += ScheduleApply;
        model.SelectionChanged += ScheduleApply;
        model.PreviewChanged += () => { FinishGesture(); UpdateCamera(); Apply(); };
        Rebuild();
    }

    public void CancelGesture() { _drag = null; _orbit = null; ReleaseMouseCapture(); _model?.CancelDrag(); Apply(); }
    public void FinishGesture()
    {
        if (_drag is null) return;
        _drag = null; _model?.EndDrag(); ReleaseMouseCapture(); Apply();
    }
    public void DefaultView() { _yaw = -18; _pitch = 34; _width = 10.8; UpdateCamera(); }

    internal (double CenterError, double HomeError, double TipHeight) Probe(string id) => _parts[id].Probe();

    private void Rebuild()
    {
        foreach (var part in _parts.Values) part.Dispose();
        _parts.Clear();
        _owners.Clear();
        _world.Children.Clear();
        if (_model?.Document is not { } document) return;
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Token("DarkHardwareAmbientLightColor", Colors.Gray)));
        lights.Children.Add(new DirectionalLight(Token("DarkHardwareKeyLightColor", Colors.White), new Vector3D(-1, -3, -2)));
        _world.Children.Add(new ModelVisual3D { Content = lights });
        _base = new ChamberBaseVisual3D { Length = document.BaseLength, Width = document.BaseWidth, Thickness = 0.12 };
        Theme(_base);
        _world.Children.Add(_base);
        foreach (var part in document.Parts)
        {
            var visual = new PartVisual(this, part);
            _parts.Add(part.Id, visual);
            _owners.Add(visual.Root, part);
            _base.Attachments.Add(visual.Root);
        }
        UpdateCamera();
        Apply();
    }

    private void ScheduleApply()
    {
        if (_queued) return;
        _queued = true;
        Dispatcher.InvokeAsync(() => { _queued = false; Apply(); }, DispatcherPriority.Render);
    }

    public void Apply()
    {
        if (_model?.Document is not { } document) return;
        var disk = document.Parts.First(p => p.Kind == "Disk");
        foreach (var part in _parts.Values) part.Apply(_model, disk);
        DrawMarkers();
    }

    private void UpdateCamera()
    {
        bool editing = _model?.IsPreview != true;
        var target = new Point3D(0, 0.3, 0);
        if (editing)
        {
            _camera.Position = new Point3D(0, 15, 0);
            _camera.LookDirection = new Vector3D(0, -1, 0);
            _camera.UpDirection = new Vector3D(0, 0, -1);
        }
        else
        {
            double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
            var offset = new Vector3D(12 * Math.Cos(pitch) * Math.Sin(yaw), 12 * Math.Sin(pitch), 12 * Math.Cos(pitch) * Math.Cos(yaw));
            _camera.Position = target + offset;
            _camera.LookDirection = -offset;
            _camera.UpDirection = new Vector3D(0, 1, 0);
        }
        _camera.Width = _width * Math.Max(1, ActualHeight > 0 ? ActualWidth / ActualHeight / 1.35 : 1);
        DrawMarkers();
    }

    private (Vector3D Forward, Vector3D Right, Vector3D Up) Basis()
    {
        var f = _camera.LookDirection; f.Normalize();
        var r = Vector3D.CrossProduct(f, _camera.UpDirection); r.Normalize();
        return (f, r, Vector3D.CrossProduct(r, f));
    }

    public Point Project(Point3D world)
    {
        var (_, r, u) = Basis();
        var delta = world - _camera.Position;
        double height = _camera.Width * Math.Max(1, ActualHeight) / Math.Max(1, ActualWidth);
        return new Point((0.5 + Vector3D.DotProduct(delta, r) / _camera.Width) * ActualWidth,
            (0.5 - Vector3D.DotProduct(delta, u) / height) * ActualHeight);
    }

    private Point OnPlane(Point point, double y)
    {
        var (f, r, u) = Basis();
        double height = _camera.Width * ActualHeight / Math.Max(1, ActualWidth);
        var origin = _camera.Position + r * ((point.X / Math.Max(1, ActualWidth) - 0.5) * _camera.Width)
            + u * ((0.5 - point.Y / Math.Max(1, ActualHeight)) * height);
        var world = origin + f * ((y - origin.Y) / f.Y);
        return new Point(world.X, world.Z);
    }

    private void DrawMarkers()
    {
        _overlay.Children.Clear();
        _labels.Clear();
        if (_model is null) return;
        foreach (var part in _model.Parts.OrderBy(p => ReferenceEquals(p, _model.Selected)))
        {
            var position = Project(new Point3D(part.X, part.Y, part.Z));
            bool selected = ReferenceEquals(part, _model.Selected);
            var marker = new Border { Background = ResourceBrush(selected ? "DarkAccentPressed" : "DarkControlBackground"),
                BorderBrush = ResourceBrush(selected ? "DarkAccent" : "DarkBorderBrush"), BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock { Text = part.Id, Foreground = ResourceBrush("DarkPrimaryText") } };
            ((TextBlock)marker.Child).SetResourceReference(TextBlock.FontSizeProperty, "Size14");
            marker.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var bounds = new Rect(position.X + 12, position.Y + 10, marker.DesiredSize.Width, marker.DesiredSize.Height);
            while (_labels.Values.Any(r => r.IntersectsWith(bounds))) bounds.Y += bounds.Height + 4;
            _labels[part] = bounds;
            Canvas.SetLeft(marker, bounds.X);
            Canvas.SetTop(marker, bounds.Y);
            _overlay.Children.Add(marker);
        }
    }

    private ChamberPlacement? Hit(Point point)
    {
        foreach (var (part, bounds) in _labels.Reverse()) if (bounds.Contains(point)) return part;
        ChamberPlacement? result = null;
        VisualTreeHelper.HitTest(_viewport, null, hit =>
        {
            if (hit is RayHitTestResult ray)
                for (DependencyObject? node = ray.VisualHit; node is not null; node = VisualTreeHelper.GetParent(node))
                    if (_owners.TryGetValue(node, out result)) return HitTestResultBehavior.Stop;
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(point));
        return result;
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (_model is null) return;
        var point = e.GetPosition(this);
        var part = Hit(point);
        if (part is null) return;
        _model.Selected = part;
        if (!_model.IsPreview)
        {
            _model.BeginDrag();
            _drag = OnPlane(point, part.Y);
            _startX = part.X; _startZ = part.Z;
            CaptureMouse();
        }
        e.Handled = true;
    }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_drag is { } start && _model?.Selected is { } part)
        {
            var current = OnPlane(point, part.Y);
            double x = Math.Clamp(_startX + current.X - start.X, -9, 9), z = Math.Clamp(_startZ + current.Y - start.Y, -9, 9);
            part.X = _model.Snap ? Math.Round(x / 0.05) * 0.05 : x;
            part.Z = _model.Snap ? Math.Round(z / 0.05) * 0.05 : z;
            e.Handled = true;
        }
        else if (_orbit is { } previous)
        {
            _yaw -= (point.X - previous.X) * 0.35;
            _pitch = Math.Clamp(_pitch + (point.Y - previous.Y) * 0.25, 8, 89);
            _orbit = point;
            UpdateCamera();
        }
    }
    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is null) return;
        _drag = null;
        _model!.EndDrag();
        ReleaseMouseCapture();
        Apply();
        e.Handled = true;
    }
    private void OnOrbitDown(object sender, MouseButtonEventArgs e)
    {
        if (_model?.IsPreview != true) return;
        _orbit = e.GetPosition(this); CaptureMouse(); e.Handled = true;
    }

    private Color Token(string key, Color fallback) => TryFindResource(key) is Color color ? color : fallback;
    private Brush ResourceBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
    private void Theme(HardwareVisual3D visual)
    {
        visual.BodyColor = Token("DarkHardwareBodyColor", Colors.Gray);
        visual.HighlightColor = Token("DarkHardwareHighlightColor", Colors.DodgerBlue);
    }

    private sealed class PartVisual : IDisposable
    {
        private const double Scale = 0.7;
        private readonly ChamberLayoutScene _scene;
        private readonly ChamberPlacement _part;
        private readonly List<HardwareVisual3D> _hardware = [];
        private readonly List<FluidPipeVisual3D> _pipes = [];
        private readonly List<(DependencyPropertyDescriptor Descriptor, object Source, EventHandler Handler)> _listeners = [];
        private readonly ArmVisual3D? _arm;
        private readonly LiftVisual3D? _lift;
        private readonly HomeCupVisual3D? _cup;
        private readonly DiskVisual3D? _disk;
        private readonly BowlVisual3D? _bowl;
        private readonly DoorVisual3D? _door;
        private ChamberPlacement? _target;
        private bool _applying;
        public ModelVisual3D Root { get; } = new();

        public PartVisual(ChamberLayoutScene scene, ChamberPlacement part)
        {
            _scene = scene; _part = part;
            switch (part.Kind)
            {
                case "Disk": _disk = Add(new DiskVisual3D { IsDiskVisible = true, Radius = 1.32, SpindleHeight = 0.32 }); break;
                case "Bowl": _bowl = Add(new BowlVisual3D { Radius = 1.59 }); break;
                case "Door": _door = Add(new DoorVisual3D { Width = 2.2, Height = 0.58 }); break;
                case "ArmAssembly":
                    if (part.HasLift) _lift = Add(new LiftVisual3D());
                    _arm = Add(new ArmVisual3D());
                    _cup = Add(new HomeCupVisual3D());
                    for (int i = 0; i < part.Nozzles; i++)
                    {
                        var pipe = new FluidPipeVisual3D { Transform = new TranslateTransform3D(0, 0.1, (i - (part.Nozzles - 1) / 2d) * 0.15) };
                        scene.Theme(pipe); _arm.Attachments.Add(pipe); _pipes.Add(pipe);
                    }
                    Listen(_arm, ArmVisual3D.AngleProperty);
                    if (_lift is not null) Listen(_lift, LiftVisual3D.MountHeightProperty);
                    break;
            }
        }

        private T Add<T>(T visual) where T : HardwareVisual3D
        { _scene.Theme(visual); _hardware.Add(visual); Root.Children.Add(visual); return visual; }

        public void Apply(ChamberEditorViewModel model, ChamberPlacement target)
        {
            _applying = true;
            _target = target;
            bool preview = model.IsPreview;
            bool animate = preview && _scene.IsVisible;
            double scale = _part.IsArm ? Scale : 1;
            var transform = new Transform3DGroup();
            transform.Children.Add(new ScaleTransform3D(scale, scale, scale));
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), _part.Yaw)));
            transform.Children.Add(new TranslateTransform3D(_part.X, _part.Y, _part.Z));
            Root.Transform = transform;
            foreach (var hardware in _hardware) hardware.IsSelected = ReferenceEquals(model.Selected, _part);
            if (_disk is not null) _disk.RotationSpeed = animate && model.Spin ? 100 : 0;
            if (_bowl is not null) _bowl.IsRaised = preview && model.Lift;
            if (_door is not null) _door.IsOpen = preview && model.Lift;
            if (_arm is not null && _cup is not null)
            {
                double dx = target.X - _part.X, dz = target.Z - _part.Z;
                double distance = Math.Max(0.8, Math.Sqrt(dx * dx + dz * dz));
                if (!model.IsDragging || _arm.Length < 0.1) _arm.Length = distance / Scale;
                double yaw = _part.Yaw * Math.PI / 180;
                double localX = Math.Cos(yaw) * dx - Math.Sin(yaw) * dz;
                double localZ = Math.Sin(yaw) * dx + Math.Cos(yaw) * dz;
                double center = Math.Atan2(-localZ, localX) * 180 / Math.PI;
                double sweep = ((center - _part.HomeAngle) % 360 + 540) % 360 - 180;
                double edgeSweep = 2 * Math.Asin(Math.Clamp(1.32 / (2 * distance), 0, 1)) * 180 / Math.PI;
                double edge = _part.HomeAngle + sweep - Math.Sign(sweep) * Math.Min(Math.Abs(sweep), edgeSweep);
                double reach = preview ? model.Reach : 0;
                double angle = reach <= 0.5 ? _part.HomeAngle + (edge - _part.HomeAngle) * reach * 2 : edge + (_part.HomeAngle + sweep - edge) * (reach - 0.5) * 2;
                if (Math.Abs(_arm.Angle - angle) > 1e-6)
                {
                    double from = _arm.Angle;
                    _arm.BeginAnimation(ArmVisual3D.AngleProperty, null);
                    _arm.Angle = angle;
                    if (animate && !model.IsDragging) _arm.BeginAnimation(ArmVisual3D.AngleProperty,
                        new DoubleAnimation(from, angle, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop });
                }
                if (_lift is not null) _lift.IsRaised = preview && model.Lift;
                double home = _part.HomeAngle * Math.PI / 180;
                _cup.Transform = new TranslateTransform3D(_arm.Length * Math.Cos(home), 0, -_arm.Length * Math.Sin(home));
                foreach (var pipe in _pipes) { pipe.Length = _arm.Length; pipe.IsFlowing = preview && model.Spray; pipe.IsAnimationEnabled = animate; }
                _cup.IsDraining = preview && model.Spray && reach < 0.001;
            }
            _applying = false;
            UpdateStreams();
        }

        private void Listen(DependencyObject source, DependencyProperty property)
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(property, source.GetType());
            EventHandler handler = (_, _) => UpdateStreams();
            descriptor.AddValueChanged(source, handler);
            _listeners.Add((descriptor, source, handler));
        }
        public (double CenterError, double HomeError, double TipHeight) Probe()
        {
            double angle = _arm!.Angle * Math.PI / 180;
            var tip = _arm.TransformToAncestor(_scene._base!).Transform(new Point3D(_arm.Length * Math.Cos(angle), 0, -_arm.Length * Math.Sin(angle)));
            var cup = _cup!.TransformToAncestor(_scene._base!).Transform(new Point3D());
            return (Math.Sqrt(Math.Pow(tip.X - _target!.X, 2) + Math.Pow(tip.Z - _target.Z, 2)),
                Math.Sqrt(Math.Pow(tip.X - cup.X, 2) + Math.Pow(tip.Z - cup.Z, 2)), tip.Y);
        }
        private void UpdateStreams()
        {
            if (_applying || _arm is null || _target is null || _scene._base is null) return;
            _arm.Transform = new TranslateTransform3D(0, 0.1275 + (_lift?.MountHeight ?? 0.85), 0);
            foreach (var pipe in _pipes)
            {
                var transform = pipe.TransformToAncestor(_scene._base);
                var nozzle = transform.Transform(new Point3D(pipe.Length, -FluidPipeVisual3D.OutletDrop, 0));
                var below = transform.Transform(new Point3D(pipe.Length, -FluidPipeVisual3D.OutletDrop - 1, 0));
                double landing = 0;
                if (Math.Abs(_arm.Angle - _part.HomeAngle) < 1 && _cup is not null)
                    landing = _cup.TransformToAncestor(_scene._base).Transform(new Point3D(0, 0.41, 0)).Y;
                else if (Math.Pow(nozzle.X - _target.X, 2) + Math.Pow(nozzle.Z - _target.Z, 2) < 1.32 * 1.32)
                    landing = _target.Y + 0.046;
                double length = Math.Max(0, (nozzle.Y - landing) / Math.Max(0.001, nozzle.Y - below.Y));
                if (Math.Abs(pipe.StreamLength - length) > 0.0001) pipe.StreamLength = length;
            }
        }
        public void Dispose()
        {
            foreach (var (descriptor, source, handler) in _listeners) descriptor.RemoveValueChanged(source, handler);
            foreach (var pipe in _pipes) pipe.IsAnimationEnabled = false;
            if (_disk is not null) _disk.RotationSpeed = 0;
            _arm?.BeginAnimation(ArmVisual3D.AngleProperty, null);
        }
    }
}
