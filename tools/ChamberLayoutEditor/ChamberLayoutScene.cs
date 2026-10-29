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

public enum ChamberViewPreset { Perspective, Top, Front, Back, Left, Right }

/// <summary>编辑、预览共用原生三维组件；编辑安装位置和模拟反馈完全分开。</summary>
public sealed class ChamberLayoutScene : Grid
{
    private readonly Grid _stage = new() { RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly ScaleTransform _aspectCompensation = new();
    private readonly Viewport3D _viewport = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly OrthographicCamera _camera = new();
    private readonly Dictionary<DependencyObject, ChamberPlacement> _owners = [];
    private readonly Dictionary<string, PartVisual> _parts = [];
    private readonly Dictionary<ChamberPlacement, Rect> _labels = [];
    private readonly ModelVisual3D _world = new();
    private ChamberEditorViewModel? _model;
    private ChamberBaseVisual3D? _base;
    private Point3D? _drag;
    private Vector3D _dragNormal;
    private Point? _orbit;
    private double _startX, _startY, _startZ;
    private double _yaw = -18, _pitch = 34, _width = 10.8;
    private ChamberViewPreset _viewPreset = ChamberViewPreset.Perspective;
    private bool _queued;
    public ChamberViewPreset ViewPreset => _viewPreset;
    public event Action<ChamberViewPreset>? ViewPresetChanged;

    public ChamberLayoutScene()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        _viewport.Camera = _camera;
        _viewport.Children.Add(_world);
        _stage.RenderTransform = _aspectCompensation;
        _stage.Children.Add(_viewport);
        _stage.Children.Add(_overlay);
        Children.Add(_stage);
        PreviewMouseLeftButtonDown += OnPointerDown;
        PreviewMouseLeftButtonUp += OnPointerUp;
        PreviewMouseRightButtonDown += OnOrbitDown;
        PreviewMouseRightButtonUp += (_, e) => { _orbit = null; ReleaseMouseCapture(); e.Handled = true; };
        PreviewMouseMove += OnPointerMove;
        PreviewMouseWheel += (_, e) => { FinishGesture(); _width = Math.Clamp(_width - e.Delta / 120d * 0.5, 5, 48); UpdateCamera(); e.Handled = true; };
        LostMouseCapture += (_, _) => { if (_drag is not null) { _drag = null; _model?.CancelDrag(); Apply(); } _orbit = null; };
        SizeChanged += (_, _) => { UpdateCamera(); UpdateAspectCompensation(); };
        Loaded += (_, _) => UpdateAspectCompensation();
        LayoutUpdated += (_, _) => UpdateAspectCompensation();
        IsVisibleChanged += (_, _) => { UpdateAspectCompensation(); Apply(); };
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
    public void DefaultView()
    {
        FinishGesture();
        _viewPreset = ChamberViewPreset.Perspective;
        _yaw = -18; _pitch = 34;
        _width = Math.Max(10.8, Math.Max(_model?.Document?.BaseLength ?? 0, _model?.Document?.BaseWidth ?? 0) * 1.3);
        UpdateCamera();
        ViewPresetChanged?.Invoke(_viewPreset);
    }

    public void SetViewPreset(ChamberViewPreset preset)
    {
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        if (_viewPreset == preset) return;
        FinishGesture();
        _viewPreset = preset;
        (_yaw, _pitch) = preset switch
        {
            ChamberViewPreset.Top => (0, 89),
            ChamberViewPreset.Front => (0, 0),
            ChamberViewPreset.Back => (180, 0),
            ChamberViewPreset.Left => (-90, 0),
            ChamberViewPreset.Right => (90, 0),
            _ => (-18, 34)
        };
        UpdateCamera();
        ViewPresetChanged?.Invoke(_viewPreset);
    }

    internal (double CenterError, double HomeError, double TipHeight) Probe(string id) => _parts[id].Probe();
    internal IReadOnlyDictionary<string, double> ProbeDimensions(string id) => _parts[id].ProbeDimensions();
    internal (double Length, double Width, double Thickness) ProbeBase() => (_base!.Length, _base.Width, _base.Thickness);
    internal Point3D ProbeOrigin(string id) => _parts[id].Root.Transform.Transform(new Point3D());
    internal ChamberPlacement? Pick(Point point) => Hit(point);
    internal (double X, double Y) AspectCompensation => (_aspectCompensation.ScaleX, _aspectCompensation.ScaleY);

    // 宿主可能使用非等比 Viewbox.Fill。只缩放三维场景与标记，不改变逻辑尺寸、配置和宿主布局。
    private void UpdateAspectCompensation()
    {
        if (!IsVisible || ActualWidth <= 0 || ActualHeight <= 0
            || PresentationSource.FromVisual(this) is not { RootVisual: Visual root, CompositionTarget: { } target }) return;
        var transform = TransformToAncestor(root);
        var origin = target.TransformToDevice.Transform(transform.Transform(new Point()));
        var x = target.TransformToDevice.Transform(transform.Transform(new Point(1, 0))) - origin;
        var y = target.TransformToDevice.Transform(transform.Transform(new Point(0, 1))) - origin;
        double sx = x.Length, sy = y.Length;
        if (!double.IsFinite(sx) || !double.IsFinite(sy) || sx < 1e-8 || sy < 1e-8) return;
        double scale = Math.Min(sx, sy);
        double fx = scale / sx, fy = scale / sy;
        if (Math.Abs(fx - _aspectCompensation.ScaleX) < 1e-7 && Math.Abs(fy - _aspectCompensation.ScaleY) < 1e-7) return;
        FinishGesture();
        _aspectCompensation.ScaleX = fx;
        _aspectCompensation.ScaleY = fy;
    }

    internal Point ProjectToRoot(Point3D world)
    {
        var root = (Visual)PresentationSource.FromVisual(this).RootVisual;
        return _viewport.TransformToAncestor(root).Transform(Project(world));
    }

    internal Point3D UnprojectFromRoot(Point point, Point3D anchor)
    {
        var root = (Visual)PresentationSource.FromVisual(this).RootVisual;
        var inverse = _viewport.TransformToAncestor(root).Inverse!;
        return Unproject(inverse.Transform(point), anchor);
    }

    private void Rebuild()
    {
        foreach (var part in _parts.Values) part.Dispose();
        _parts.Clear();
        _owners.Clear();
        _world.Children.Clear();
        _base = null;
        if (_model?.Document is not { } document) return;
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Token("DarkHardwareAmbientLightColor", Colors.Gray)));
        lights.Children.Add(new DirectionalLight(Token("DarkHardwareKeyLightColor", Colors.White), new Vector3D(-1, -3, -2)));
        _world.Children.Add(new ModelVisual3D { Content = lights });
        foreach (var part in document.Parts.OrderBy(p => p.IsBase ? 0 : 1))
        {
            var visual = new PartVisual(this, part);
            _parts.Add(part.Id, visual);
            _owners.Add(visual.Root, part);
            _world.Children.Add(visual.Root);
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
        var target = new Point3D(0, 0.3, 0);
        if (_viewPreset == ChamberViewPreset.Top)
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

    private Vector3D DragNormal()
    {
        var f = Basis().Forward;
        if (Math.Abs(f.Y) > 0.15) return new Vector3D(0, 1, 0);
        return Math.Abs(f.X) > Math.Abs(f.Z) ? new Vector3D(1, 0, 0) : new Vector3D(0, 0, 1);
    }

    private Point3D OnPlane(Point point, Point3D anchor, Vector3D normal)
    {
        var (f, r, u) = Basis();
        double height = _camera.Width * ActualHeight / Math.Max(1, ActualWidth);
        var origin = _camera.Position + r * ((point.X / Math.Max(1, ActualWidth) - 0.5) * _camera.Width)
            + u * ((0.5 - point.Y / Math.Max(1, ActualHeight)) * height);
        return origin + f * (Vector3D.DotProduct(anchor - origin, normal) / Vector3D.DotProduct(f, normal));
    }

    internal Point3D Unproject(Point point, Point3D anchor) => OnPlane(point, anchor, DragNormal());

    private void DrawMarkers()
    {
        _overlay.Children.Clear();
        _labels.Clear();
        if (_model is null) return;
        foreach (var part in _model.Parts.OrderBy(p => ReferenceEquals(p, _model.Selected)))
        {
            var anchor = part.IsBase ? _parts[part.Id].Root.Transform.Transform(new Point3D(-part.Size("Length") / 2, 0, part.Size("Width") / 2))
                : new Point3D(part.X, part.Y, part.Z);
            var position = Project(anchor);
            bool selected = ReferenceEquals(part, _model.Selected);
            var marker = new Border { Background = ResourceBrush(selected ? "DarkAccentPressed" : "DarkControlBackground"),
                BorderBrush = ResourceBrush(selected ? "DarkAccent" : "DarkBorderBrush"), BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock { Text = part.DisplayName, Foreground = ResourceBrush("DarkPrimaryText") } };
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
        var point = e.GetPosition(_viewport);
        var part = Hit(point);
        if (part is null) return;
        _model.Selected = part;
        if (!_model.IsPreview)
        {
            _model.BeginDrag();
            _dragNormal = DragNormal();
            _drag = OnPlane(point, new Point3D(part.X, part.Y, part.Z), _dragNormal);
            _startX = part.X; _startY = part.Y; _startZ = part.Z;
            CaptureMouse();
        }
        e.Handled = true;
    }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(_viewport);
        if (_drag is { } start && _model?.Selected is { } part)
        {
            var current = OnPlane(point, new Point3D(_startX, _startY, _startZ), _dragNormal);
            double Snap(double value, double limit)
            {
                value = Math.Clamp(value, -limit, limit);
                return _model.Snap ? Math.Round(value / 0.05) * 0.05 : value;
            }
            if (Math.Abs(_dragNormal.X) < 0.5) part.X = Snap(_startX + current.X - start.X, 9);
            if (Math.Abs(_dragNormal.Y) < 0.5) part.Y = Snap(_startY + current.Y - start.Y, 3);
            if (Math.Abs(_dragNormal.Z) < 0.5) part.Z = Snap(_startZ + current.Z - start.Z, 9);
            e.Handled = true;
        }
        else if (_orbit is { } previous)
        {
            _yaw -= (point.X - previous.X) * 0.35;
            _pitch = Math.Clamp(_pitch + (point.Y - previous.Y) * 0.25, -85, 89);
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
        FinishGesture();
        _viewPreset = ChamberViewPreset.Perspective;
        ViewPresetChanged?.Invoke(_viewPreset);
        _orbit = e.GetPosition(_viewport); CaptureMouse(); e.Handled = true;
    }

    private Color Token(string key, Color fallback) => TryFindResource(key) is Color color ? color : fallback;
    private Brush ResourceBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
    private void Theme(HardwareVisual3D visual)
    {
        visual.BodyColor = Token("DarkHardwareBodyColor", Colors.Gray);
        visual.HighlightColor = Token("DarkHardwareHighlightColor", Colors.DodgerBlue);
    }

    private double BaseSurfaceHeight(Point3D point)
    {
        double landing = 0;
        bool found = false;
        foreach (var part in _model?.Parts.Where(p => p.IsBase) ?? [])
        {
            if (part.Y > point.Y || _parts[part.Id].Root.Transform.Inverse is not { } inverse) continue;
            var local = inverse.Transform(new Point3D(point.X, part.Y, point.Z));
            if (Math.Abs(local.X) <= part.Size("Length") / 2 && Math.Abs(local.Z) <= part.Size("Width") / 2)
            {
                if (!found || part.Y > landing) landing = part.Y;
                found = true;
            }
        }
        return landing;
    }

    // 组件分别挂在同一坐标系，底盘的位置和旋转不传递给其他组件；硬件根节点负责通知后代动画的挂载生命周期。
    private sealed class PlacementRoot : HardwareVisual3D { }

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
        private readonly ChamberBaseVisual3D? _plate;
        private readonly FluidPipeVisual3D? _nozzle;
        private ChamberPlacement? _target;
        private bool _applying;
        public ModelVisual3D Root { get; } = new PlacementRoot();

        public PartVisual(ChamberLayoutScene scene, ChamberPlacement part)
        {
            _scene = scene; _part = part;
            switch (part.Kind)
            {
                case "Base": _plate = Add(new ChamberBaseVisual3D()); scene._base ??= _plate; break;
                case "Disk": _disk = Add(new DiskVisual3D { IsDiskVisible = true, Radius = 1.32, SpindleHeight = 0.32 }); break;
                case "Bowl": _bowl = Add(new BowlVisual3D { Radius = 1.59 }); break;
                case "Door": _door = Add(new DoorVisual3D { Width = 2.2, Height = 0.58 }); break;
                case "Lift": _lift = Add(new LiftVisual3D()); break;
                case "HomeCup": _cup = Add(new HomeCupVisual3D()); break;
                case "Nozzle": _nozzle = Add(new FluidPipeVisual3D()); break;
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
            double scaleX = _part.Kind == "Lift" ? _part.Size("WidthScale") : scale;
            double scaleY = _part.Kind is "Lift" or "Bowl" ? _part.Size("HeightScale")
                : _part.Kind == "Nozzle" ? _part.Size("DiameterScale") : scale;
            double scaleZ = _door is not null ? _part.Size("DepthScale")
                : _part.Kind == "Lift" ? _part.Size("WidthScale")
                : _part.Kind == "Nozzle" ? _part.Size("DiameterScale") : scale;
            var transform = new Transform3DGroup();
            transform.Children.Add(new ScaleTransform3D(scaleX, scaleY, scaleZ));
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), _part.Yaw)));
            transform.Children.Add(new TranslateTransform3D(_part.X, _part.Y, _part.Z));
            Root.Transform = transform;
            foreach (var hardware in _hardware) hardware.IsSelected = ReferenceEquals(model.Selected, _part);
            if (_plate is not null)
            {
                _plate.Length = _part.Size("Length"); _plate.Width = _part.Size("Width"); _plate.Thickness = _part.Size("Thickness");
            }
            if (_disk is not null)
            {
                _disk.Radius = _part.Size("Radius"); _disk.Thickness = _part.Size("Thickness");
                _disk.SpindleHeight = _part.Size("SpindleHeight"); _disk.RotationSpeed = animate && model.Spin ? 100 : 0;
            }
            if (_bowl is not null) { _bowl.Radius = _part.Size("Radius"); _bowl.IsRaised = preview && model.Lift; }
            if (_door is not null) { _door.Width = _part.Size("Width"); _door.Height = _part.Size("Height"); _door.IsOpen = preview && model.Lift; }
            if (_part.Kind == "Lift" && _lift is not null) _lift.IsRaised = preview && model.Lift;
            if (_part.Kind == "HomeCup" && _cup is not null)
            {
                _cup.Radius = _part.Size("Radius"); _cup.Height = _part.Size("Height");
                _cup.IsDraining = preview && model.Spray;
            }
            if (_nozzle is not null)
            {
                _nozzle.Length = _part.Size("Length"); _nozzle.StreamLength = _part.Size("StreamLength");
                _nozzle.IsFlowing = preview && model.Spray; _nozzle.IsAnimationEnabled = animate;
            }
            if (_arm is not null && _cup is not null)
            {
                double dx = target.X - _part.X, dz = target.Z - _part.Z;
                double distance = Math.Max(0.05, Math.Sqrt(dx * dx + dz * dz));
                double length = _part.AutoLength ? distance : _part.Size("ArmLength");
                if (!_part.AutoLength || !model.IsDragging || _arm.Length < 0.1) _arm.Length = length / Scale;
                _arm.Width = _part.Size("ArmWidth") / Scale;
                _arm.Thickness = _part.Size("ArmThickness") / Scale;
                _arm.PivotRadius = _part.Size("PivotRadius") / Scale;
                _cup.Radius = _part.Size("CupRadius") / Scale;
                _cup.Height = _part.Size("CupHeight") / Scale;
                double yaw = _part.Yaw * Math.PI / 180;
                double localX = Math.Cos(yaw) * dx - Math.Sin(yaw) * dz;
                double localZ = Math.Sin(yaw) * dx + Math.Cos(yaw) * dz;
                double center = Math.Atan2(-localZ, localX) * 180 / Math.PI;
                double sweep = ((center - _part.HomeAngle) % 360 + 540) % 360 - 180;
                double radius = target.Size("Radius");
                double cosine = (distance * distance + length * length - radius * radius) / (2 * distance * length);
                double edgeSweep = cosine is >= -1 and <= 1 ? Math.Acos(cosine) * 180 / Math.PI : 0;
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
                if (_lift is not null)
                {
                    _lift.Transform = new ScaleTransform3D(_part.Size("LiftWidthScale"), _part.Size("LiftHeightScale"), _part.Size("LiftWidthScale"));
                    _lift.IsRaised = preview && model.Lift;
                }
                double home = _part.HomeAngle * Math.PI / 180;
                _cup.Transform = new TranslateTransform3D(_arm.Length * Math.Cos(home), 0, -_arm.Length * Math.Sin(home));
                for (int i = 0; i < _pipes.Count; i++)
                {
                    var pipe = _pipes[i];
                    double diameter = _part.Size($"Nozzle{i + 1}.DiameterScale");
                    var pipeTransform = new Transform3DGroup();
                    pipeTransform.Children.Add(new ScaleTransform3D(1, diameter, diameter));
                    pipeTransform.Children.Add(new TranslateTransform3D(0, 0.1, (i - (_pipes.Count - 1) / 2d) * 0.15));
                    pipe.Transform = pipeTransform;
                    pipe.Length = _arm.Length * _part.Size($"Nozzle{i + 1}.LengthScale");
                    pipe.IsFlowing = preview && model.Spray; pipe.IsAnimationEnabled = animate;
                }
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
            var tip = _arm.TransformToAncestor(_scene._world).Transform(new Point3D(_arm.Length * Math.Cos(angle), 0, -_arm.Length * Math.Sin(angle)));
            var cup = _cup!.TransformToAncestor(_scene._world).Transform(new Point3D());
            return (Math.Sqrt(Math.Pow(tip.X - _target!.X, 2) + Math.Pow(tip.Z - _target.Z, 2)),
                Math.Sqrt(Math.Pow(tip.X - cup.X, 2) + Math.Pow(tip.Z - cup.Z, 2)), tip.Y);
        }
        public IReadOnlyDictionary<string, double> ProbeDimensions()
        {
            double Measure(ModelVisual3D visual, double x, double y, double z)
            {
                var transform = visual.TransformToAncestor(_scene._world);
                return (transform.Transform(new Point3D(x, y, z)) - transform.Transform(new Point3D())).Length;
            }
            var result = new Dictionary<string, double>();
            if (_plate is not null) { result["Length"] = _plate.Length; result["Width"] = _plate.Width; result["Thickness"] = _plate.Thickness; }
            if (_arm is not null && _cup is not null)
            {
                result["ArmLength"] = Measure(_arm, _arm.Length, 0, 0);
                result["ArmWidth"] = Measure(_arm, 0, 0, _arm.Width);
                result["CupRadius"] = Measure(_cup, _cup.Radius, 0, 0);
                result["CupHeight"] = Measure(_cup, 0, _cup.Height, 0);
                for (int i = 0; i < _pipes.Count; i++)
                {
                    result[$"Nozzle{i + 1}.Length"] = Measure(_pipes[i], _pipes[i].Length, 0, 0);
                    result[$"Nozzle{i + 1}.DiameterScale"] = Measure(_pipes[i], 0, 1, 0) / Scale;
                }
            }
            if (_disk is not null) { result["Radius"] = Measure(_disk, _disk.Radius, 0, 0); result["Thickness"] = Measure(_disk, 0, _disk.Thickness, 0); }
            if (_bowl is not null) result["Radius"] = Measure(_bowl, _bowl.Radius, 0, 0);
            if (_door is not null) { result["Width"] = Measure(_door, _door.Width, 0, 0); result["Height"] = Measure(_door, 0, _door.Height, 0); }
            if (_part.Kind == "Lift" && _lift is not null) { result["WidthScale"] = Measure(_lift, 1, 0, 0); result["HeightScale"] = Measure(_lift, 0, 1, 0); }
            if (_part.Kind == "HomeCup" && _cup is not null) { result["Radius"] = Measure(_cup, _cup.Radius, 0, 0); result["Height"] = Measure(_cup, 0, _cup.Height, 0); }
            if (_nozzle is not null) { result["Length"] = Measure(_nozzle, _nozzle.Length, 0, 0); result["DiameterScale"] = Measure(_nozzle, 0, 1, 0); }
            return result;
        }
        private void UpdateStreams()
        {
            if (_applying || _arm is null || _target is null || _scene._base is null) return;
            _arm.Transform = new TranslateTransform3D(0, _part.Size("ArmThickness") / Scale * 1.5
                + (_lift is not null ? _lift.MountHeight * _part.Size("LiftHeightScale") : 0.85), 0);
            bool draining = false;
            foreach (var pipe in _pipes)
            {
                var transform = pipe.TransformToAncestor(_scene._world);
                var nozzle = transform.Transform(new Point3D(pipe.Length, -FluidPipeVisual3D.OutletDrop, 0));
                var below = transform.Transform(new Point3D(pipe.Length, -FluidPipeVisual3D.OutletDrop - 1, 0));
                double landing = _scene.BaseSurfaceHeight(nozzle);
                bool hitsCup = false;
                if (Math.Abs(_arm.Angle - _part.HomeAngle) < 1 && _cup is not null)
                {
                    var cupCenter = _cup.TransformToAncestor(_scene._world).Transform(new Point3D(0, _cup.Height * (0.41 / 0.655), 0));
                    hitsCup = Math.Pow(nozzle.X - cupCenter.X, 2) + Math.Pow(nozzle.Z - cupCenter.Z, 2) < Math.Pow(_cup.Radius * Scale, 2);
                    if (hitsCup) { landing = cupCenter.Y; draining |= pipe.IsFlowing && nozzle.Y >= landing; }
                }
                if (!hitsCup && Math.Pow(nozzle.X - _target.X, 2) + Math.Pow(nozzle.Z - _target.Z, 2) < Math.Pow(_target.Size("Radius"), 2))
                    landing = _target.Y + _target.Size("Thickness") + 0.001;
                double length = Math.Max(0, (nozzle.Y - landing) / Math.Max(0.001, nozzle.Y - below.Y));
                if (Math.Abs(pipe.StreamLength - length) > 0.0001) pipe.StreamLength = length;
            }
            if (_cup is not null) _cup.IsDraining = draining;
        }
        public void Dispose()
        {
            foreach (var (descriptor, source, handler) in _listeners) descriptor.RemoveValueChanged(source, handler);
            foreach (var pipe in _pipes) pipe.IsAnimationEnabled = false;
            if (_nozzle is not null) _nozzle.IsAnimationEnabled = false;
            if (_disk is not null) _disk.RotationSpeed = 0;
            _arm?.BeginAnimation(ArmVisual3D.AngleProperty, null);
        }
    }
}
