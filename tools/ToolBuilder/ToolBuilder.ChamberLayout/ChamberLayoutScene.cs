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
    private double _yaw = -18, _pitch = 34, _width = 7.8;
    private ChamberViewPreset _viewPreset = ChamberViewPreset.Perspective;
    private bool _queued;
    private bool _applyingScene;
    private bool _updatingAttachments;
    private Point3D _dragAnchor;
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
        _width = Math.Max(7.8, Math.Max(_model?.Document?.BaseLength ?? 0, _model?.Document?.BaseWidth ?? 0) * 1.3);
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
    internal (double Angle, bool Raised, bool Spray, bool Spin, bool Open) ProbeMotion(string id) => _parts[id].ProbeMotion();
    internal (double Length, double Width, double Thickness) ProbeBase() => (_base!.Length, _base.Width, _base.Thickness);
    internal Point3D ProbeOrigin(string id) => _parts[id].Origin;
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
        _applyingScene = true;
        foreach (var part in document.Parts.OrderBy(p => p.IsBase ? 0 : 1))
        {
            var visual = new PartVisual(this, part);
            _parts.Add(part.Id, visual);
            _owners.Add(visual.Root, part);
        }
        foreach (var visual in _parts.Values) visual.Attach();
        _applyingScene = false;
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
        var disk = document.Parts.FirstOrDefault(p => p.Kind == "Disk");
        _applyingScene = true;
        try
        {
            foreach (var part in _parts.Values) part.Apply(_model, disk);
            foreach (var part in _parts.Values) part.ApplyArm(_model);
        }
        finally { _applyingScene = false; }
        UpdateAttachments();
        DrawMarkers();
    }

    private void UpdateAttachments()
    {
        if (_applyingScene || _updatingAttachments) return;
        _updatingAttachments = true;
        try
        {
            foreach (var part in _parts.Values) part.UpdateMount();
            foreach (var part in _parts.Values) part.UpdateStreams();
            foreach (var part in _parts.Values) part.UpdateCup();
        }
        finally { _updatingAttachments = false; }
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
                : _parts[part.Id].Origin;
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
            _dragAnchor = _parts[part.Id].Origin;
            _drag = OnPlane(point, _dragAnchor, _dragNormal);
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
            var current = OnPlane(point, _dragAnchor, _dragNormal);
            var delta = _parts[part.Id].PlacementDelta(current - start);
            double Snap(double value, double limit)
            {
                value = Math.Clamp(value, -limit, limit);
                return _model.Snap ? Math.Round(value / 0.05) * 0.05 : value;
            }
            part.X = Snap(_startX + delta.X, 9);
            part.Y = Snap(_startY + delta.Y, 3);
            part.Z = Snap(_startZ + delta.Z, 9);
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
        private const double ArmScale = 0.7;
        private readonly ChamberLayoutScene _scene;
        private readonly ChamberPlacement _part;
        private readonly List<HardwareVisual3D> _hardware = [];
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
        private bool _hitsCup;
        public ModelVisual3D Root { get; } = new PlacementRoot();
        private PartVisual? Parent => _part.ParentId.Length > 0 ? _scene._parts[_part.ParentId] : null;
        private double ParentScale => Parent is null ? 1 : ArmScale;
        public Point3D Origin => Root.TransformToAncestor(_scene._world).Transform(new Point3D());

        public PartVisual(ChamberLayoutScene scene, ChamberPlacement part)
        {
            _scene = scene; _part = part;
            switch (part.Kind)
            {
                case "Base": _plate = Add(new ChamberBaseVisual3D()); scene._base ??= _plate; break;
                case "Disk": _disk = Add(new DiskVisual3D { IsDiskVisible = true }); break;
                case "Bowl": _bowl = Add(new BowlVisual3D()); break;
                case "Door": _door = Add(new DoorVisual3D()); break;
                case "Lift": _lift = Add(new LiftVisual3D()); Listen(_lift, LiftVisual3D.MountHeightProperty); break;
                case "HomeCup": _cup = Add(new HomeCupVisual3D()); break;
                case "Nozzle": _nozzle = Add(new FluidPipeVisual3D()); break;
                case "Arm": _arm = Add(new ArmVisual3D()); Listen(_arm, ArmVisual3D.AngleProperty); break;
            }
        }
        private T Add<T>(T visual) where T : HardwareVisual3D
        { _scene.Theme(visual); _hardware.Add(visual); Root.Children.Add(visual); return visual; }
        public void Attach()
        {
            if (Parent is not { } parent) _scene._world.Children.Add(Root);
            else if (_nozzle is not null) parent._arm!.Attachments.Add(Root);
            else parent.Root.Children.Add(Root);
        }
        public Vector3D PlacementDelta(Vector3D delta)
        {
            if (VisualTreeHelper.GetParent(Root) is not ModelVisual3D parent) return delta;
            var inverse = parent.TransformToAncestor(_scene._world).Inverse!;
            return (inverse.Transform(new Point3D(delta.X, delta.Y, delta.Z)) - inverse.Transform(new Point3D())) * ParentScale;
        }
        private void UpdatePlacement()
        {
            double sx = _part.IsArm ? ArmScale : _lift is not null ? _part.Size("WidthScale") : 1;
            double sy = _bowl is not null || _lift is not null ? _part.Size("HeightScale") : _nozzle is not null ? _part.Size("DiameterScale") : sx;
            double sz = _door is not null ? _part.Size("DepthScale") : _nozzle is not null ? _part.Size("DiameterScale") : sx;
            double x = _part.X / ParentScale, y = _part.Y / ParentScale, z = _part.Z / ParentScale;
            if (_cup is not null && Parent is { _arm: { } arm } parent)
            {
                double home = parent._part.HomeAngle * Math.PI / 180;
                var mount = parent.Mount();
                x += mount.X + arm.Length * Math.Cos(home); z += mount.Z - arm.Length * Math.Sin(home);
            }
            var transform = new Transform3DGroup();
            transform.Children.Add(new ScaleTransform3D(sx, sy, sz));
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), _part.Yaw)));
            transform.Children.Add(new TranslateTransform3D(x, y, z)); Root.Transform = transform;
        }
        private Point3D Mount()
        {
            var support = _scene._parts.Values.FirstOrDefault(p => p._part.ParentId == _part.Id && p._lift is not null);
            return support is null ? new Point3D(0, 0.85, 0) : support.Root.Transform.Transform(new Point3D(0, support._lift!.MountHeight, 0));
        }
        public void Apply(ChamberEditorViewModel model, ChamberPlacement? target)
        {
            _target = target;
            bool preview = model.IsPreview, animate = preview && _scene.IsVisible;
            var motion = model.MotionFor(_part.Id);
            UpdatePlacement();
            foreach (var visual in _hardware) visual.IsSelected = ReferenceEquals(model.Selected, _part);
            if (_plate is not null) { _plate.Length = _part.Size("Length"); _plate.Width = _part.Size("Width"); _plate.Thickness = _part.Size("Thickness"); }
            if (_disk is not null)
            {
                _disk.Radius = _part.Size("Radius"); _disk.Thickness = _part.Size("Thickness");
                _disk.SpindleHeight = _part.Size("SpindleHeight"); _disk.RotationSpeed = animate && motion.Spin ? 100 : 0;
            }
            if (_bowl is not null) { _bowl.Radius = _part.Size("Radius"); _bowl.IsRaised = preview && motion.Raised; }
            if (_door is not null) { _door.Width = _part.Size("Width"); _door.Height = _part.Size("Height"); _door.IsOpen = preview && motion.Open; }
            if (_lift is not null) _lift.IsRaised = preview && motion.Raised;
            if (_cup is not null) { _cup.Radius = _part.Size("Radius") / ParentScale; _cup.Height = _part.Size("Height") / ParentScale; }
            if (_nozzle is not null)
            {
                _nozzle.IsFlowing = preview && motion.Spray; _nozzle.IsAnimationEnabled = animate;
                _nozzle.StreamLength = _part.Size("StreamLength") / ParentScale;
            }
        }
        public void ApplyArm(ChamberEditorViewModel model)
        {
            if (_arm is null) return;
            var mount = Mount();
            var pivot = Root.Transform.Transform(mount);
            double dx = _target is null ? _part.Size("ArmLength") : _target.X - pivot.X;
            double dz = _target is null ? 0 : _target.Z - pivot.Z;
            double length = _part.AutoLength ? Math.Max(0.05, Math.Sqrt(dx * dx + dz * dz)) : _part.Size("ArmLength");
            _arm.Length = length / ArmScale; _arm.Width = _part.Size("ArmWidth") / ArmScale;
            _arm.Thickness = _part.Size("ArmThickness") / ArmScale; _arm.PivotRadius = _part.Size("PivotRadius") / ArmScale;
            double yaw = _part.Yaw * Math.PI / 180;
            double center = Math.Atan2(-(Math.Sin(yaw) * dx + Math.Cos(yaw) * dz), Math.Cos(yaw) * dx - Math.Sin(yaw) * dz) * 180 / Math.PI;
            double sweep = ((center - _part.HomeAngle) % 360 + 540) % 360 - 180;
            double reach = model.IsPreview ? model.MotionFor(_part.Id).Reach : 0;
            double angle = _part.HomeAngle + sweep * reach;
            if (Math.Abs(_arm.Angle - angle) <= 1e-6) return;
            double from = _arm.Angle; _arm.BeginAnimation(ArmVisual3D.AngleProperty, null); _arm.Angle = angle;
            if (model.IsPreview && _scene.IsVisible && !model.IsDragging)
                _arm.BeginAnimation(ArmVisual3D.AngleProperty, new DoubleAnimation(from, angle, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop });
        }
        public void UpdateMount()
        {
            if (_arm is not null)
            {
                var mount = Mount(); _arm.Transform = new TranslateTransform3D(mount.X, mount.Y + _part.Size("ArmThickness") / ArmScale * 1.5, mount.Z);
            }
            if (_cup is not null) UpdatePlacement();
            if (_nozzle is not null)
            {
                double length = _part.AutoLength && Parent?._arm is { } arm ? arm.Length * ArmScale : _part.Size("Length");
                _nozzle.Length = length * _part.Size("LengthScale") / ParentScale;
            }
        }
        public void UpdateStreams()
        {
            if (_nozzle is null) return;
            _hitsCup = false;
            if (Parent is not { } parent) return;
            var transform = _nozzle.TransformToAncestor(_scene._world);
            var tip = transform.Transform(new Point3D(_nozzle.Length, -FluidPipeVisual3D.OutletDrop, 0));
            var below = transform.Transform(new Point3D(_nozzle.Length, -FluidPipeVisual3D.OutletDrop - 1, 0));
            double landing = _scene.BaseSurfaceHeight(tip);
            var cup = _scene._parts.Values.FirstOrDefault(p => p._part.ParentId == parent._part.Id && p._cup is not null);
            if (cup is not null && Math.Abs(parent._arm!.Angle - parent._part.HomeAngle) < 1)
            {
                var center = cup._cup!.TransformToAncestor(_scene._world).Transform(new Point3D(0, cup._cup.Height * (0.41 / 0.655), 0));
                if (Math.Pow(tip.X - center.X, 2) + Math.Pow(tip.Z - center.Z, 2) < Math.Pow(cup._part.Size("Radius"), 2))
                { landing = center.Y; _hitsCup = true; }
            }
            if (!_hitsCup && _target is { } disk && Math.Pow(tip.X - disk.X, 2) + Math.Pow(tip.Z - disk.Z, 2) < Math.Pow(disk.Size("Radius"), 2))
                landing = disk.Y + disk.Size("Thickness") + 0.001;
            double stream = Math.Max(0, (tip.Y - landing) / Math.Max(0.001, tip.Y - below.Y));
            if (Math.Abs(_nozzle.StreamLength - stream) > 0.0001) _nozzle.StreamLength = stream;
        }
        public void UpdateCup()
        {
            if (_cup is not null) _cup.IsDraining = _scene._parts.Values.Any(p => p._part.ParentId == _part.ParentId && p._hitsCup && p._nozzle?.IsFlowing == true);
        }
        private void Listen(DependencyObject source, DependencyProperty property)
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(property, source.GetType());
            EventHandler handler = (_, _) => _scene.UpdateAttachments(); descriptor.AddValueChanged(source, handler);
            _listeners.Add((descriptor, source, handler));
        }
        public (double CenterError, double HomeError, double TipHeight) Probe()
        {
            double radians = _arm!.Angle * Math.PI / 180;
            var tip = _arm.TransformToAncestor(_scene._world).Transform(new Point3D(_arm.Length * Math.Cos(radians), 0, -_arm.Length * Math.Sin(radians)));
            var cup = _scene._parts.Values.FirstOrDefault(p => p._part.ParentId == _part.Id && p._cup is not null);
            var home = cup?.Origin ?? tip;
            return (_target is null ? 0 : Math.Sqrt(Math.Pow(tip.X - _target.X, 2) + Math.Pow(tip.Z - _target.Z, 2)),
                Math.Sqrt(Math.Pow(tip.X - home.X, 2) + Math.Pow(tip.Z - home.Z, 2)), tip.Y);
        }
        public (double Angle, bool Raised, bool Spray, bool Spin, bool Open) ProbeMotion() =>
            (_arm?.Angle ?? 0, _lift?.IsRaised ?? _bowl?.IsRaised ?? false, _nozzle?.IsFlowing ?? false, (_disk?.RotationSpeed ?? 0) > 0, _door?.IsOpen ?? false);
        public IReadOnlyDictionary<string, double> ProbeDimensions()
        {
            var result = new Dictionary<string, double>();
            if (_arm is not null) { result["ArmLength"] = _arm.Length * ArmScale; result["ArmWidth"] = _arm.Width * ArmScale; }
            if (_nozzle is not null) { result["Length"] = _nozzle.Length * ParentScale; result["DiameterScale"] = _part.Size("DiameterScale"); }
            if (_lift is not null) { result["WidthScale"] = _part.Size("WidthScale"); result["HeightScale"] = _part.Size("HeightScale"); }
            if (_cup is not null) { result["Radius"] = _part.Size("Radius"); result["Height"] = _part.Size("Height"); }
            if (_bowl is not null) result["Radius"] = _bowl.Radius;
            if (_disk is not null) { result["Radius"] = _disk.Radius; result["Thickness"] = _disk.Thickness; }
            if (_door is not null) { result["Width"] = _door.Width; result["Height"] = _door.Height; }
            if (_plate is not null) { result["Length"] = _plate.Length; result["Width"] = _plate.Width; result["Thickness"] = _plate.Thickness; }
            return result;
        }
        public void Dispose()
        {
            foreach (var (descriptor, source, handler) in _listeners) descriptor.RemoveValueChanged(source, handler);
            if (_nozzle is not null) _nozzle.IsAnimationEnabled = false;
            if (_disk is not null) _disk.RotationSpeed = 0;
            _arm?.BeginAnimation(ArmVisual3D.AngleProperty, null);
        }
    }
}
