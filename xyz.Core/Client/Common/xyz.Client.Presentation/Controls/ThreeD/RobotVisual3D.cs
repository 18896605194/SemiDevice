using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 搬运机械手三维外观。使用原 RobotViewBase.ArmVisual 的每帧几何和原 XAML 手叉轮廓，晶圆直接使用 DiskVisual3D。
/// 不计算运动顺序、不订阅设备、不创建相机；可放入共享 Viewport3D。
/// </summary>
public sealed class RobotVisual3D : HardwareVisual3D
{
    /// <summary>原 84 像素晶圆直径对应框架 DiskVisual3D 默认直径 2.64。</summary>
    public const double UnitsPerPixel = 2.64 / RobotViewBase.WaferDiameter;

    private static readonly ResourceDictionary SharedResources = new()
    {
        Source = new Uri("/xyz.Client.Presentation;component/Styles/RobotResources.xaml", UriKind.Relative),
    };

    private readonly Part _assembly = new("BaseBrush");
    private readonly Part _turret = new("TurretBrush");
    private readonly List<ArmRig> _rigs = [];
    private readonly AxisAngleRotation3D _rotation = new(new Vector3D(0, -1, 0), 0);
    private readonly TranslateTransform3D _travel = new();
    private RobotArmType? _type;
    private Geometry? _fork;
    private MeshGeometry3D? _forkMesh;

    public RobotVisual3D()
    {
        var placement = new Transform3DGroup();
        placement.Children.Add(new ScaleTransform3D(UnitsPerPixel, UnitsPerPixel, UnitsPerPixel));
        placement.Children.Add(_travel);
        _assembly.Transform = placement;
        Children.Add(_assembly);
        _assembly.Add(HardwareMesh3D.Cylinder(58, 8, 0));
        _assembly.Add(HardwareMesh3D.Cylinder(52, 2, 8), 1.15);
        _assembly.Add(HardwareMesh3D.Cylinder(43, 4, 10), 0.85);
        _turret.Transform = new RotateTransform3D(_rotation);
        _assembly.Children.Add(_turret);
    }

    /// <summary>接收原控件已算好的显示帧；尺寸、轮廓变化才换网格，其余只改变换和晶圆绑定。</summary>
    public void SetPose(RobotArmType type, double rotation, double travel,
        IReadOnlyList<RobotViewBase.ArmVisual> arms)
    {
        ArgumentNullException.ThrowIfNull(arms);
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (!double.IsFinite(rotation)) throw new ArgumentOutOfRangeException(nameof(rotation));
        if (!double.IsFinite(travel)) throw new ArgumentOutOfRangeException(nameof(travel));
        var fork = (Geometry)SharedResources[type == RobotArmType.Linear ? "LinearForkGeometry" : "FrogLegForkGeometry"];
        _rotation.Angle = rotation;
        _travel.OffsetX = travel * UnitsPerPixel;
        if (_type != type)
        {
            _type = type;
            _turret.Clear();
            var outline = (Geometry)SharedResources[type == RobotArmType.Linear ? "RobotLinearTurretGeometry" : "RobotFrogLegTurretGeometry"];
            _turret.Add(Extrude(outline, 8)).Transform = new TranslateTransform3D(0, 14, 0);
            _turret.Add(HardwareMesh3D.Cylinder(12, 3, 22), 1.2);
        }

        if (!ReferenceEquals(fork, _fork))
        {
            _fork = fork;
            _forkMesh = Extrude(fork, 1.5);
            foreach (var rig in _rigs) rig.SetFork(_forkMesh);
        }

        while (_rigs.Count > arms.Count)
        {
            var last = _rigs[^1];
            last.Disk.Data = null;
            _assembly.Children.Remove(last.Root);
            _rigs.RemoveAt(_rigs.Count - 1);
        }

        while (_rigs.Count < arms.Count)
        {
            var rig = new ArmRig(_forkMesh!);
            _rigs.Add(rig); _assembly.Children.Add(rig.Root);
        }

        for (int i = 0; i < arms.Count; i++) _rigs[i].Update(arms[i], type, i);
    }

    private static Color ColorOf(string key)
    {
        object resource = SharedResources[key];
        if (resource is SolidColorBrush brush) return brush.Color;
        var stops = ((GradientBrush)resource).GradientStops;
        return Color.FromRgb((byte)stops.Average(stop => stop.Color.R),
            (byte)stops.Average(stop => stop.Color.G), (byte)stops.Average(stop => stop.Color.B));
    }

    private sealed class Part : HardwareVisual3D
    {
        public Part(string colorKey = "MetalBrush")
        {
            BodyColor = ColorOf(colorKey);
            HighlightColor = HardwareTheme.ColorOf("DarkAccent", HighlightColor);
        }

        public GeometryModel3D Add(MeshGeometry3D mesh, double brightness = 1) => AddMesh(mesh, brightness);
        public void Clear() => ClearMeshes();
        public void Show(GeometryModel3D mesh, bool visible)
        {
            if (visible && !Model.Children.Contains(mesh)) Model.Children.Add(mesh);
            else if (!visible) Model.Children.Remove(mesh);
        }
    }

    private sealed class ArmRig
    {
        public Part Root { get; } = new();
        public DiskVisual3D Disk { get; } = new() { Radius = 42, Thickness = 0.045 / UnitsPerPixel };
        private readonly Part _linear = new(), _frog = new(), _fork = new("ForkBrush"), _pads = new("PadBrush");
        private readonly ModelVisual3D _end = new();
        private readonly ModelVisual3D _linearMount = new(), _frogMount = new();
        private readonly GeometryModel3D _outer, _inner;
        private readonly GeometryModel3D[] _links = new GeometryModel3D[4];
        private readonly GeometryModel3D[] _joints = new GeometryModel3D[6];
        private readonly RotateTransform3D _heading = new(new AxisAngleRotation3D(new Vector3D(0, -1, 0), 0));
        private readonly TranslateTransform3D _stack = new();
        private readonly TranslateTransform3D _height = new();
        private readonly TranslateTransform3D _wafer = new();
        private readonly AxisAngleRotation3D _counter = new(new Vector3D(0, 1, 0), 0);
        private RobotArmType? _type;
        private readonly TextBlock _number = new() { Width = 68, Height = 48, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.Bold };
        private readonly Viewport2DVisual3D _numberFace = new();
        private readonly TranslateTransform3D _numberPosition = new(0, 5, 50);

        public ArmRig(MeshGeometry3D fork)
        {
            var placement = new Transform3DGroup();
            placement.Children.Add(_stack); placement.Children.Add(_heading); placement.Children.Add(_height);
            Root.Transform = placement;
            Root.Children.Add(_linearMount); Root.Children.Add(_frogMount); Root.Children.Add(_end);
            _linearMount.Children.Add(_linear); _frogMount.Children.Add(_frog);
            _outer = _linear.Add(HardwareMesh3D.ChamferedBox(new Point3D(-9, -3, 0), 18, 4, 1), .8);
            _inner = _linear.Add(HardwareMesh3D.ChamferedBox(new Point3D(-6, 1, 0), 12, 3, 1), 1.2);
            _end.Transform = _wafer;
            _end.Children.Add(_fork);
            _end.Children.Add(_pads);
            SetFork(fork);
            _linear.Add(HardwareMesh3D.ChamferedBox(new Point3D(-17, -2, 38), 34, 6, 24), 1.05)
                .Transform = _wafer;
            for (int i = 0; i < 4; i++)
            {
                double width = i < 2 ? 16 : 12;
                _links[i] = _frog.Add(HardwareMesh3D.ChamferedBox(new Point3D(-width / 2, i < 2 ? 0 : 3, 0), width, 4, 1), i < 2 ? .9 : 1.15);
            }

            for (int i = 0; i < 6; i++) _joints[i] = _frog.Add(HardwareMesh3D.Cylinder(i < 2 ? 10 : 5.5, 7, 0), 1.25);
            _frog.Add(HardwareMesh3D.ChamferedBox(new Point3D(-24, -2, 47), 48, 6, 22), 1.05)
                .Transform = _wafer;
            var diskPlacement = new Transform3DGroup();
            diskPlacement.Children.Add(new RotateTransform3D(_counter));
            diskPlacement.Children.Add(new TranslateTransform3D(0, 2.6, 0));
            Disk.Transform = diskPlacement;
            _end.Children.Add(Disk);
            _number.SetResourceReference(TextBlock.FontSizeProperty, "Size26");
            var numberMesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { new(-15, 0, -9), new(-15, 0, 9), new(15, 0, 9), new(15, 0, -9) },
                TextureCoordinates = new PointCollection { new(0, 0), new(0, 1), new(1, 1), new(1, 0) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
            };
            numberMesh.Freeze();
            var numberMaterial = new DiffuseMaterial(Brushes.White);
            Viewport2DVisual3D.SetIsVisualHostMaterial(numberMaterial, true);
            _numberFace.Geometry = numberMesh; _numberFace.Material = numberMaterial; _numberFace.Visual = _number;
            var numberPlacement = new Transform3DGroup();
            numberPlacement.Children.Add(new RotateTransform3D(_counter)); numberPlacement.Children.Add(_numberPosition);
            _numberFace.Transform = numberPlacement; _end.Children.Add(_numberFace);
        }

        public void SetFork(MeshGeometry3D fork)
        {
            _fork.Clear();
            _fork.Add(fork);
            _pads.Clear();
            foreach (var (x, z) in new[] { (-20d, -30d), (20d, -30d), (0d, 23d) })
            {
                _pads.Add(HardwareMesh3D.Cylinder(3, 1, 1.5)).Transform = new TranslateTransform3D(x, 0, z);
            }
        }

        public void Update(RobotViewBase.ArmVisual frame, RobotArmType type, int index)
        {
            ((AxisAngleRotation3D)_heading.Rotation).Angle = frame.Angle;
            _counter.Angle = frame.Angle;
            _stack.OffsetX = _stack.OffsetZ = frame.StackOffset;
            _height.OffsetY = 25 + index * 7;
            _wafer.OffsetZ = frame.WaferTop;
            Disk.Data = frame.Wafer;
            _number.Text = frame.Arm.ToString();
            if (frame.IsExtended) _number.SetResourceReference(TextBlock.ForegroundProperty, "DarkAccent");
            else _number.Foreground = (Brush)SharedResources["ArmNumberBrush"];
            _numberPosition.OffsetZ = type == RobotArmType.Linear ? 50 : 58;
            // 盘底放在手叉吸盘上；盘面和片号仍由框架 Wafer 绘制。
            if (_type != type)
            {
                _linearMount.Children.Clear(); _frogMount.Children.Clear();
                if (type == RobotArmType.Linear) _linearMount.Children.Add(_linear);
                else _frogMount.Children.Add(_frog);
                _type = type;
            }
            if (type == RobotArmType.Linear)
            {
                bool railsVisible = frame.RailVisibility == Visibility.Visible;
                _linear.Show(_outer, railsVisible);
                _linear.Show(_inner, railsVisible);
                if (railsVisible)
                {
                    _outer.Transform = Stretch(frame.RailOuterHeight, frame.RailOuterTop);
                    _inner.Transform = Stretch(frame.RailInnerHeight, frame.RailInnerTop);
                }
            }
            else
            {
                Point[] starts = [new(-20, 10), new(20, 10), new(frame.ElbowLeftX, frame.ElbowLeftY), new(frame.ElbowRightX, frame.ElbowRightY)];
                Point[] ends = [starts[2], starts[3], new(-12, frame.PivotY), new(12, frame.PivotY)];
                for (int i = 0; i < 4; i++) _links[i].Transform = Link(starts[i], ends[i]);
                Point[] joints = [starts[0], starts[1], starts[2], starts[3], ends[2], ends[3]];
                for (int i = 0; i < joints.Length; i++) _joints[i].Transform = new TranslateTransform3D(joints[i].X, 0, joints[i].Y);
            }
        }

        private static Transform3D Stretch(double length, double z)
        {
            var group = new Transform3DGroup();
            group.Children.Add(new ScaleTransform3D(1, 1, length));
            group.Children.Add(new TranslateTransform3D(0, 0, z));
            return group;
        }

        private static Transform3D Link(Point start, Point end)
        {
            Vector delta = end - start;
            var group = new Transform3DGroup();
            group.Children.Add(new ScaleTransform3D(1, 1, delta.Length));
            group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), Math.Atan2(delta.X, delta.Y) * 180 / Math.PI)));
            group.Children.Add(new TranslateTransform3D(start.X, 0, start.Y));
            return group;
        }
    }

    // 对原 XAML 的 U 形手叉轮廓做耳切三角化并挤出厚度；不再另画近似的两根长条。
    private static MeshGeometry3D Extrude(Geometry source, double thickness)
    {
        var figure = source.GetFlattenedPathGeometry(.12, ToleranceType.Absolute).Figures.Single();
        var points = new List<Point> { figure.StartPoint };
        foreach (var segment in figure.Segments)
        {
            if (segment is PolyLineSegment poly) points.AddRange(poly.Points);
            else if (segment is LineSegment line) points.Add(line.Point);
        }
        if ((points[0] - points[^1]).Length < .00001) points.RemoveAt(points.Count - 1);
        double area = points.Select((p, i) => p.X * points[(i + 1) % points.Count].Y - points[(i + 1) % points.Count].X * p.Y).Sum();
        if (area < 0) points.Reverse();
        var mesh = new MeshGeometry3D();
        Point3D At(int index, double y) => new(points[index].X, y, points[index].Y);
        void Triangle(Point3D a, Point3D b, Point3D c)
        {
            var n = Vector3D.CrossProduct(b - a, c - a);
            if (n.LengthSquared < 1e-12) return;
            n.Normalize(); int index = mesh.Positions.Count;
            foreach (var p in new[] { a, b, c }) { mesh.Positions.Add(p); mesh.Normals.Add(n); mesh.TriangleIndices.Add(index++); }
        }
        double Cross(Point a, Point b, Point c) => Vector.CrossProduct(b - a, c - b);
        bool Inside(Point p, Point a, Point b, Point c) => Cross(a, b, p) >= -1e-8 && Cross(b, c, p) >= -1e-8 && Cross(c, a, p) >= -1e-8;
        var remaining = Enumerable.Range(0, points.Count).ToList();
        while (remaining.Count > 2)
        {
            bool found = false;
            for (int k = 0; k < remaining.Count; k++)
            {
                int a = remaining[(k + remaining.Count - 1) % remaining.Count], b = remaining[k], c = remaining[(k + 1) % remaining.Count];
                if (Cross(points[a], points[b], points[c]) <= 1e-8 || remaining.Any(j => j != a && j != b && j != c && Inside(points[j], points[a], points[b], points[c]))) continue;
                Triangle(At(a, thickness), At(c, thickness), At(b, thickness));
                Triangle(At(a, 0), At(b, 0), At(c, 0));
                remaining.RemoveAt(k); found = true; break;
            }
            if (!found) throw new InvalidOperationException("Robot fork outline cannot be triangulated.");
        }
        for (int i = 0; i < points.Count; i++)
        {
            int j = (i + 1) % points.Count;
            Triangle(At(i, 0), At(j, 0), At(j, thickness));
            Triangle(At(i, 0), At(j, thickness), At(i, thickness));
        }
        mesh.Freeze(); return mesh;
    }
}
