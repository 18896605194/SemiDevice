using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using xyz.Client.Common.Log;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 腔体三维图：底座上装 Bowl、旋转盘、腔门和每条摆臂（Lift 立柱、摆臂、喷嘴管、Home 接液杯）。
/// 装哪些部件看 Parts（后端按 sc.xml 推来的部件组成），状态全部绑定显示模型；盘上的片绑 Wafer（晶圆账）。
/// 摆臂收到新位置后 0.2 s 过渡过去。门、Bowl、Lift 跟到位反馈走：到位画在那一头，未知（命令发了、到位信号还没亮）画在行程中间并高亮。
/// 不注册逐帧事件：液柱长度只在摆臂角度、Lift 高度或出液变化时重算，只有动画进行中角度和高度才会逐帧变，停下来就一点不算。
/// 左键拖动旋转视角、滚轮缩放，图下面一条工具栏：默认视角、俯视。
/// </summary>
public partial class ChamberScene : UserControl
{
    #region 装配尺寸（场景示意单位）

    private const double BowlRadius = 1.59;
    private const double DiskRadius = 1.32;

    /// <summary>
    /// 旋转盘（卡盘）底面离安装面的高度，下面主轴撑着：比 Bowl 降下时的上沿（1 级 0.275）高、比升起时（0.825）低——
    /// Bowl 降下露出盘面好取放片，升起来才把盘围住挡液（用户点名的关系）。
    /// </summary>
    private const double DiskHeight = 0.32;

    private const double DoorWidth = 2.2;
    private const double DoorHeight = 0.58;

    /// <summary>腔门在底座前沿（+Z）。</summary>
    private const double DoorOffset = 2.42;

    /// <summary>摆臂、Lift 立柱、接液杯整体缩小的比例。</summary>
    private const double ArmScale = 0.7;

    /// <summary>摆臂回转中心的安装位置：第 1 条在 Bowl 右侧（+X），第 2 条在左侧（-X），都靠后（-Z）。</summary>
    private const double ArmMountX = 1.95;

    private const double ArmMountZ = -1.25;

    /// <summary>左右各一个安装位，三维图最多画两条摆臂。</summary>
    private const int MaxArms = 2;

    /// <summary>摆臂回转座底面到臂局部原点的距离（1.5 倍臂厚）：把回转座放到 Lift 顶面上。</summary>
    private const double ArmSeatOffset = 0.1275;

    /// <summary>管子在臂面上方的高度（臂局部单位）。</summary>
    private const double PipeHeight = 0.1;

    /// <summary>相邻两路喷嘴的横向间距（臂局部单位）；几路喷嘴左右对称排开，中点正好在臂中线上。</summary>
    private const double PipeSpacing = 0.15;

    /// <summary>Home 位摆角：臂尖朝前（+Z），接液杯就放在那儿。</summary>
    private const double HomeAngle = -90;

    /// <summary>摆角离 Home 不到 1° 算在 Home：液柱落进接液杯、杯亮起来。</summary>
    private const double HomeAngleTolerance = 1;

    /// <summary>液柱落到接液杯里收液锥面附近（杯局部坐标）。</summary>
    private const double CupLandingHeight = 0.41;

    /// <summary>液柱停在盘面上方一点点，免得跟盘面重叠闪烁。</summary>
    private const double SurfaceGap = 0.001;

    /// <summary>液柱长度变化小于它就不改（改一次管子要重建网格）。</summary>
    private const double StreamTolerance = 0.0001;

    /// <summary>收到新位置后摆臂 0.2 s 过渡过去，免得一跳一跳（用户定的：三维里的动画一律 0.2 s，写死）。</summary>
    private const int ArmTransitionMilliseconds = 200;

    /// <summary>旋转盘的显示转速（度 / 秒）：只表示"在转"，不跟实际转速走——真实几百转画出来只会频闪。</summary>
    private const double SpinDisplaySpeed = 100;

    #endregion

    #region 视角

    private const double DefaultYaw = -18;
    private const double DefaultPitch = 34;
    private const double DefaultZoom = 9.6;
    private const double TopZoom = 12;
    private const double MinPitch = 12;
    private const double MaxPitch = 89;
    private const double MinZoom = 4.8;
    private const double MaxZoom = 12;

    /// <summary>拖动一像素转多少度：横向转方位、纵向转俯仰。</summary>
    private const double DragYawPerPixel = 0.35;

    private const double DragPitchPerPixel = 0.25;

    /// <summary>滚轮一格的 Delta（Windows 约定 120）和一格缩放多少。</summary>
    private const double WheelNotch = 120;

    private const double ZoomPerNotch = 0.35;

    /// <summary>正交相机离观察点的距离：只影响裁剪，不影响大小（大小看 Width）。</summary>
    private const double CameraDistance = 10;

    /// <summary>
    /// 视野宽度（_zoom）是按这个宽高比定的：图比它更矮更宽时正交相机按高度撑开视野，免得腔体上下被切掉（手动页三维区就是矮宽的）。
    /// </summary>
    private const double FitAspect = 2.3;

    private static readonly Point3D CameraTarget = new(0, 0.28, 0.1);

    #endregion

    private const string LogModule = "ChamberScene";

    private readonly BowlVisual3D _bowl;
    private readonly DiskVisual3D _disk;
    private readonly DoorVisual3D _door;
    private readonly List<ArmRig> _rigs = [];
    private double _yaw = DefaultYaw;
    private double _pitch = DefaultPitch;
    private double _zoom = DefaultZoom;
    private Point? _dragFrom;

    public ChamberScene()
    {
        InitializeComponent();
        _bowl = new BowlVisual3D { Radius = BowlRadius };
        _disk = new DiskVisual3D
        {
            IsDiskVisible = true,
            Radius = DiskRadius,
            SpindleHeight = DiskHeight,
            Transform = new TranslateTransform3D(0, DiskHeight, 0),
        };
        _door = new DoorVisual3D
        {
            Width = DoorWidth,
            Height = DoorHeight,
            Transform = new TranslateTransform3D(0, 0, DoorOffset),
        };
        ApplyTheme(_bowl);
        ApplyTheme(_door);
        ApplyTheme(_disk);
        if (TryFindResource("DarkHardwareDiskFill") is Brush fill)
        {
            _disk.FillColor = fill;
        }

        if (TryFindResource("DarkHardwareDiskBorder") is Brush border)
        {
            _disk.BorderBrush = border;
        }

        BindingOperations.SetBinding(_disk, DiskVisual3D.DataProperty,
            new Binding(nameof(Wafer)) { Source = this, Mode = BindingMode.OneWay });
        IsVisibleChanged += OnVisibleChanged;
        Viewport.SizeChanged += (_, _) => UpdateCamera();
        UpdateCamera();
        Rebuild();
    }

    #region 依赖属性

    /// <summary>部件显示模型（门、Bowl、旋转电机、摆臂）；组成变了（Revision）重搭三维图。</summary>
    public ChamberPartsModel? Parts
    {
        get => (ChamberPartsModel?)GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    public static readonly DependencyProperty PartsProperty = DependencyProperty.Register(
        nameof(Parts), typeof(ChamberPartsModel), typeof(ChamberScene), new PropertyMetadata(null, OnPartsChanged));

    /// <summary>盘上的片（晶圆账，WaferModel）；null 显示空盘。</summary>
    public object? Wafer
    {
        get => GetValue(WaferProperty);
        set => SetValue(WaferProperty, value);
    }

    public static readonly DependencyProperty WaferProperty = DependencyProperty.Register(
        nameof(Wafer), typeof(object), typeof(ChamberScene), new PropertyMetadata(null));

    private static void OnPartsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var scene = (ChamberScene)sender;
        if (args.OldValue is ChamberPartsModel old)
        {
            old.PropertyChanged -= scene.OnPartsPropertyChanged;
        }

        if (args.NewValue is ChamberPartsModel parts)
        {
            parts.PropertyChanged += scene.OnPartsPropertyChanged;
        }

        scene.BindFixedParts();
        scene.Rebuild();
    }

    private void OnPartsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ChamberPartsModel.Revision))
        {
            Rebuild();
        }
    }

    #endregion

    #region 搭场景

    /// <summary>门、Bowl、旋转盘一直是这几个实例，换 Parts 时重新绑到新模型上；Parts 为空就解绑。</summary>
    private void BindFixedParts()
    {
        var parts = Parts;
        Bind(_door, DoorVisual3D.IsOpenProperty, parts, "Door.IsOpen");
        Bind(_door, DoorVisual3D.IsUnknownProperty, parts, "Door.IsUnknown");
        Bind(_bowl, BowlVisual3D.IsRaisedProperty, parts, "Bowl.IsOpen");
        Bind(_bowl, BowlVisual3D.IsUnknownProperty, parts, "Bowl.IsUnknown");
        Bind(_disk, DiskVisual3D.RotationSpeedProperty, parts, "Spin.IsSpinning", SpinSpeedConverter.Instance);
        Bind(_disk, DiskVisual3D.RotateClockwiseProperty, parts, "Spin.IsClockwise");
    }

    /// <summary>
    /// 按现在的部件组成重搭：sc 里配了门、Bowl 才装，旋转盘一直在；摆臂按先后放右、左两个安装位。
    /// 旧摆臂先解绑、停动画，再整个拆掉。
    /// </summary>
    private void Rebuild()
    {
        foreach (var rig in _rigs)
        {
            rig.Detach();
        }

        _rigs.Clear();
        Base.Attachments.Clear();
        var parts = Parts;
        if (parts is not null && parts.Bowl.IsPresent)
        {
            Base.Attachments.Add(_bowl);
        }

        Base.Attachments.Add(_disk);
        if (parts is null)
        {
            return;
        }

        if (parts.Door.IsPresent)
        {
            Base.Attachments.Add(_door);
        }

        if (parts.Arms.Count > MaxArms)
        {
            ClientLog.Warn(LogModule, L10n.Get("chamberscene.too_many_arms", parts.Module, parts.Arms.Count, MaxArms));
        }

        int count = Math.Min(parts.Arms.Count, MaxArms);
        for (int i = 0; i < count; i++)
        {
            _rigs.Add(new ArmRig(this, parts.Arms[i], i == 0 ? 1 : -1));
        }
    }

    /// <summary>硬件用主题的金属灰和强调色（token 跟组件默认值一样，统一从主题取）。</summary>
    private void ApplyTheme(HardwareVisual3D hardware)
    {
        if (TryFindResource("DarkHardwareBodyColor") is Color body)
        {
            hardware.BodyColor = body;
        }

        if (TryFindResource("DarkAccent") is SolidColorBrush accent)
        {
            hardware.HighlightColor = accent.Color;
        }
    }

    private static void Bind(DependencyObject target, DependencyProperty property, object? source, string path,
        IValueConverter? converter = null)
    {
        if (source is null)
        {
            BindingOperations.ClearBinding(target, property);
            return;
        }

        BindingOperations.SetBinding(target, property,
            new Binding(path) { Source = source, Mode = BindingMode.OneWay, Converter = converter });
    }

    private static Transform3DGroup Group(params Transform3D[] transforms)
    {
        var group = new Transform3DGroup();
        foreach (var transform in transforms)
        {
            group.Children.Add(transform);
        }

        return group;
    }

    /// <summary>缩小到 ArmScale 后放到底座的 (x, z)。</summary>
    private static Transform3DGroup Placement(double x, double z)
    {
        return Group(new ScaleTransform3D(ArmScale, ArmScale, ArmScale), new TranslateTransform3D(x, 0, z));
    }

    #endregion

    #region 视角

    private void UpdateCamera()
    {
        double yaw = _yaw * Math.PI / 180;
        double pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(
            CameraDistance * Math.Cos(pitch) * Math.Sin(yaw),
            CameraDistance * Math.Sin(pitch),
            CameraDistance * Math.Cos(pitch) * Math.Cos(yaw));
        Camera.Position = CameraTarget + offset;
        Camera.LookDirection = -offset;
        double aspect = Viewport.ActualHeight > 0 ? Viewport.ActualWidth / Viewport.ActualHeight : FitAspect;
        Camera.Width = _zoom * Math.Max(1, aspect / FitAspect);
    }

    /// <summary>回到默认视角（斜着往下看、默认视野）。</summary>
    private void OnDefaultViewClick(object sender, RoutedEventArgs args)
    {
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _zoom = DefaultZoom;
        UpdateCamera();
    }

    private void OnTopViewClick(object sender, RoutedEventArgs args)
    {
        _yaw = 0;
        _pitch = MaxPitch;
        _zoom = TopZoom;
        UpdateCamera();
    }

    private void OnViewportMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        _dragFrom = args.GetPosition(Viewport);
        Viewport.CaptureMouse();
    }

    private void OnViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        _dragFrom = null;
        Viewport.ReleaseMouseCapture();
    }

    private void OnViewportLostMouseCapture(object sender, MouseEventArgs args)
    {
        _dragFrom = null;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs args)
    {
        var from = _dragFrom;
        if (from is null)
        {
            return;
        }

        Point current = args.GetPosition(Viewport);
        _yaw -= (current.X - from.Value.X) * DragYawPerPixel;
        _pitch = Math.Clamp(_pitch + (current.Y - from.Value.Y) * DragPitchPerPixel, MinPitch, MaxPitch);
        _dragFrom = current;
        UpdateCamera();
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs args)
    {
        _zoom = Math.Clamp(_zoom - args.Delta / WheelNotch * ZoomPerNotch, MinZoom, MaxZoom);
        UpdateCamera();
        args.Handled = true;
    }

    /// <summary>页面藏起来时摆臂直接落到目标角度，不留着动画时钟。</summary>
    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (IsVisible)
        {
            return;
        }

        foreach (var rig in _rigs)
        {
            rig.Finish();
        }
    }

    #endregion

    /// <summary>
    /// 一条摆臂的整套三维件：Lift 立柱、摆臂（带喷嘴管）、Home 接液杯，以及摆角过渡和液柱长度的计算。
    /// 摆角、Lift 高度、出液都经绑定接到自己的依赖属性上，变了才重算液柱，不注册逐帧事件。
    /// </summary>
    private sealed class ArmRig : DependencyObject
    {
        private static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
            "Angle", typeof(double), typeof(ArmRig), new PropertyMetadata(0d, OnStreamInputChanged));

        private static readonly DependencyProperty MountHeightProperty = DependencyProperty.Register(
            "MountHeight", typeof(double), typeof(ArmRig), new PropertyMetadata(0d, OnStreamInputChanged));

        private static readonly DependencyProperty FlowingProperty = DependencyProperty.Register(
            "Flowing", typeof(bool), typeof(ArmRig), new PropertyMetadata(false, OnStreamInputChanged));

        private static readonly DependencyProperty ReachProperty = DependencyProperty.Register(
            "Reach", typeof(double), typeof(ArmRig), new PropertyMetadata(0d, OnReachChanged));

        private static readonly DependencyProperty EdgeReachProperty = DependencyProperty.Register(
            "EdgeReach", typeof(double), typeof(ArmRig), new PropertyMetadata(0d, OnReachChanged));

        private readonly ChamberScene _scene;
        private readonly TranslateTransform3D _mount = new();
        private readonly List<FluidPipeVisual3D> _pipes = [];
        private readonly double _processAngle;
        private readonly double _edgeAngle;
        private double _targetAngle;
        private int _transitionVersion;
        private bool _ready;

        /// <param name="side">1 = Bowl 右侧安装位，-1 = 左侧。</param>
        public ArmRig(ChamberScene scene, ChamberArmModel model, double side)
        {
            _scene = scene;
            double x = side * ArmMountX;
            double z = ArmMountZ;
            // 臂长取回转中心到盘心的距离，工艺位角度让臂尖正对盘心（盘心在底座原点）。
            double reach = Math.Sqrt(x * x + z * z);
            _processAngle = Math.Atan2(z, -x) * 180 / Math.PI;
            // 第一个边缘：臂尖画的圆过盘心，回转中心、盘心、臂尖成等腰三角形（两腰都是臂长），
            // 臂尖离盘心正好一个盘半径时，臂比工艺位往 Home 那边少转 2·asin(半径 / 2 臂长)。
            double edgeSweep = 2 * Math.Asin(scene._disk.Radius / (2 * reach)) * 180 / Math.PI;
            _edgeAngle = _processAngle + edgeSweep * Math.Sign(HomeAngle - _processAngle);

            Lift = new LiftVisual3D { Transform = Placement(x, z) };
            Arm = new ArmVisual3D { Length = reach / ArmScale };
            BindingOperations.SetBinding(_mount, TranslateTransform3D.OffsetYProperty,
                new Binding(nameof(LiftVisual3D.MountHeight)) { Source = Lift, Mode = BindingMode.OneWay });
            Arm.Transform = Group(new TranslateTransform3D(0, ArmSeatOffset, 0), _mount,
                new ScaleTransform3D(ArmScale, ArmScale, ArmScale), new TranslateTransform3D(x, 0, z));
            int nozzles = model.Nozzles.Count;
            for (int i = 0; i < nozzles; i++)
            {
                var pipe = new FluidPipeVisual3D
                {
                    Length = Arm.Length,
                    Transform = new TranslateTransform3D(0, PipeHeight, (i - (nozzles - 1) / 2.0) * PipeSpacing),
                };
                scene.ApplyTheme(pipe);
                Bind(pipe, FluidPipeVisual3D.IsFlowingProperty, model.Nozzles[i], nameof(ChamberNozzleModel.IsOn));
                Arm.Attachments.Add(pipe);
                _pipes.Add(pipe);
            }

            // Home 时臂尖朝 +Z，接液杯放在臂尖正下方。
            Cup = new HomeCupVisual3D { Transform = Placement(x, z + reach) };
            scene.ApplyTheme(Lift);
            scene.ApplyTheme(Arm);
            scene.ApplyTheme(Cup);

            // 先绑状态、定初始角度再挂进场景：还没挂上时组件直接显示目标状态，不会从默认位置动画过来。
            Bind(Lift, LiftVisual3D.IsRaisedProperty, model.Lift, nameof(ChamberCylinderModel.IsOpen));
            Bind(Lift, LiftVisual3D.IsUnknownProperty, model.Lift, nameof(ChamberCylinderModel.IsUnknown));
            Bind(Arm, HardwareVisual3D.IsMovingProperty, model, nameof(ChamberArmModel.IsMoving));
            _targetAngle = AngleOf(model.Reach, model.EdgeReach);
            Arm.Angle = _targetAngle;
            scene.Base.Attachments.Add(Cup);
            scene.Base.Attachments.Add(Lift);
            scene.Base.Attachments.Add(Arm);

            Bind(this, AngleProperty, Arm, nameof(ArmVisual3D.Angle));
            Bind(this, MountHeightProperty, Lift, nameof(LiftVisual3D.MountHeight));
            Bind(this, FlowingProperty, model, nameof(ChamberArmModel.IsAnyNozzleOn));
            Bind(this, ReachProperty, model, nameof(ChamberArmModel.Reach));
            Bind(this, EdgeReachProperty, model, nameof(ChamberArmModel.EdgeReach));
            _ready = true;
            UpdateStreams();
        }

        public LiftVisual3D Lift { get; }

        public ArmVisual3D Arm { get; }

        public HomeCupVisual3D Cup { get; }

        private static void OnStreamInputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
            ((ArmRig)sender).UpdateStreams();

        /// <summary>位置或示教位（边缘）变了都重新算目标摆角。</summary>
        private static void OnReachChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            var rig = (ArmRig)sender;
            if (rig._ready)
            {
                rig.MoveTo(rig.AngleOf((double)rig.GetValue(ReachProperty), (double)rig.GetValue(EdgeReachProperty)));
            }
        }

        /// <summary>
        /// Reach 换成摆角：示教过边缘（0 &lt; edgeReach &lt; 1）就分两段——Home 角 → 边缘角 → 工艺位角，
        /// 轴在 Edge 时喷嘴正好画在盘边、在 Center 时正对盘心；没示教就 Home 角 → 工艺位角一段。超出两头按最近那段接着外推。
        /// </summary>
        private double AngleOf(double reach, double edgeReach)
        {
            if (edgeReach <= 0 || edgeReach >= 1)
            {
                return HomeAngle + reach * (_processAngle - HomeAngle);
            }

            if (reach <= edgeReach)
            {
                return HomeAngle + reach / edgeReach * (_edgeAngle - HomeAngle);
            }

            return _edgeAngle + (reach - edgeReach) / (1 - edgeReach) * (_processAngle - _edgeAngle);
        }

        /// <summary>从现在显示的角度 0.2 s 过渡到新角度；新位置接着来就从当前位置接着走。页面没显示时直接落位。</summary>
        private void MoveTo(double target)
        {
            _targetAngle = target;
            if (!_scene.IsVisible)
            {
                Finish();
                return;
            }

            int version = ++_transitionVersion;
            var animation = new DoubleAnimation(Arm.Angle, target, TimeSpan.FromMilliseconds(ArmTransitionMilliseconds));
            animation.Completed += (_, _) =>
            {
                if (version == _transitionVersion)
                {
                    Finish();
                }
            };
            Arm.BeginAnimation(ArmVisual3D.AngleProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        /// <summary>停掉过渡动画，直接落到目标角度。</summary>
        public void Finish()
        {
            ++_transitionVersion;
            Arm.BeginAnimation(ArmVisual3D.AngleProperty, null);
            Arm.Angle = _targetAngle;
        }

        /// <summary>拆之前解开所有绑定、停掉动画，旧模型的推送不再动这套三维件。</summary>
        public void Detach()
        {
            _ready = false;
            ++_transitionVersion;
            BindingOperations.ClearAllBindings(this);
            BindingOperations.ClearAllBindings(_mount);
            BindingOperations.ClearAllBindings(Lift);
            BindingOperations.ClearAllBindings(Arm);
            foreach (var pipe in _pipes)
            {
                BindingOperations.ClearAllBindings(pipe);
            }

            Arm.BeginAnimation(ArmVisual3D.AngleProperty, null);
        }

        /// <summary>
        /// 每根管子的液柱落点：在 Home 落进接液杯；喷口在盘面上方落到盘面；都不是就落到底座面上（开着阀摆过去就是这样）。
        /// 落点按 Disk、接液杯的实际位置算，液柱长度换回管子局部单位（整套缩小过）。在 Home 且有出液时接液杯亮起来。
        /// </summary>
        private void UpdateStreams()
        {
            if (!_ready)
            {
                return;
            }

            var plate = _scene.Base;
            var disk = _scene._disk;
            bool atHome = Math.Abs(Arm.Angle - HomeAngle) < HomeAngleTolerance;
            var diskToPlate = disk.TransformToAncestor(plate);
            var diskCenter = diskToPlate.Transform(new Point3D());
            double diskTop = diskToPlate.Transform(new Point3D(0, disk.Thickness + SurfaceGap, 0)).Y;
            double cupLanding = Cup.TransformToAncestor(plate).Transform(new Point3D(0, CupLandingHeight, 0)).Y;
            foreach (var pipe in _pipes)
            {
                var toPlate = pipe.TransformToAncestor(plate);
                var nozzle = toPlate.Transform(new Point3D(pipe.Length, -FluidPipeVisual3D.OutletDrop, 0));
                var below = toPlate.Transform(new Point3D(pipe.Length, -FluidPipeVisual3D.OutletDrop - 1, 0));
                double dx = nozzle.X - diskCenter.X;
                double dz = nozzle.Z - diskCenter.Z;
                double landing = 0;
                if (atHome)
                {
                    landing = cupLanding;
                }
                else if (dx * dx + dz * dz < disk.Radius * disk.Radius)
                {
                    landing = diskTop;
                }

                double length = Math.Max(0, (nozzle.Y - landing) / (nozzle.Y - below.Y));
                if (Math.Abs(pipe.StreamLength - length) > StreamTolerance)
                {
                    pipe.StreamLength = length;
                }
            }

            Cup.IsDraining = atHome && (bool)GetValue(FlowingProperty);
        }
    }

    /// <summary>"在转"换成显示转速：只表示转没转，不跟实际转速走。</summary>
    private sealed class SpinSpeedConverter : IValueConverter
    {
        public static readonly SpinSpeedConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? SpinDisplaySpeed : 0d;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
