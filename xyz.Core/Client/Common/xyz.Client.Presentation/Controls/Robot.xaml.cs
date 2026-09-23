using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 机械手控件（俯视）：只有机械手本体，背景透明，由页面摆放。
/// 转台旋转、水平平移、各手臂独立伸缩，支持直伸直出与蛙式两种手臂结构；机械手随控件大小等比缩放；手臂上的片用 Wafer 控件。
/// 长什么样全在 Robot.xaml 里，这里只算动到哪：绑定给目标姿态（Rotation / Travel / 各手臂 Extension），运动顺序由控件自己排——
/// 需要转向或平移时先收回全部手臂，到位后再伸出；换片时先收回的手臂收完，另一只才伸出，不会两只一起伸。
/// </summary>
public partial class Robot : UserControl
{
    #region 常量

    private const int MaxArmCount = 4;
    private const double AngleEpsilon = 0.3;
    private const double TravelEpsilon = 0.5;
    private const double ExtensionEpsilon = 0.002;

    /// <summary>
    /// 转台转一次的动画时长（毫秒），固定 0.5 s，不管转 90° 还是 180°。
    /// </summary>
    private const double TurnDurationMs = 500;

    /// <summary>
    /// 手臂伸出或收回一次的动画时长（毫秒），固定 0.5 s，不管伸缩多少。
    /// </summary>
    private const double ExtendDurationMs = 500;

    /// <summary>
    /// 水平平移一次的动画时长（毫秒），固定 0.5 s，不管移多远。
    /// </summary>
    private const double TravelDurationMs = 500;

    /// <summary>
    /// 蛙式手臂互相交叠时，没干活的手臂压暗到这个透明度。
    /// </summary>
    private const double DimmedOpacity = 0.45;

    #endregion

    #region 设计尺寸下的几何（与 Robot.xaml 里的形状同一套坐标：转台中心为原点、正前方朝上）

    /// <summary>收回到底时片心到转台中心的距离（叉停在转台上，跟转台形状配套，固定）。</summary>
    private const double RetractedReach = 24;

    /// <summary>叉根（腕部）到片心的距离。</summary>
    private const double EffectorLength = 58;

    /// <summary>蛙式连杆长度（大臂、小臂等长）。</summary>
    private const double LinkLength = 66;

    /// <summary>蛙式左右肩关节到中线的距离。</summary>
    private const double ShoulderOffset = 20;

    /// <summary>蛙式腕部两个铰点到中线的距离。</summary>
    private const double WristOffset = 12;

    /// <summary>蛙式肩关节在转台中心后方的位置。</summary>
    private const double ShoulderY = 10;

    /// <summary>直伸直出：滑座在片心后方的距离。</summary>
    private const double CarriageToWafer = 50;

    /// <summary>直伸直出：导轨露出转台前沿的位置。</summary>
    private const double TurretFront = -44;

    /// <summary>超过这个伸出量就算"伸出去了"（叠放次序、手臂号高亮用）。</summary>
    private const double ExtendedThreshold = 0.01;

    /// <summary>
    /// 片心到转台中心的距离：收回到底 RetractedReach，完全伸出 ExtendedReach，中间按伸出量线性插值。
    /// </summary>
    private double Reach(double extension)
    {
        return RetractedReach + Math.Clamp(extension, 0, 1) * (ExtendedReach - RetractedReach);
    }

    /// <summary>
    /// 收回时多只手臂叠在一起：按手臂号错开一点，下层露边；伸出过程中回到中线。
    /// </summary>
    private static double StackOffset(int count, int index, double extension)
    {
        return (count - 1 - index) * 3.5 * (1 - Math.Min(extension * 4, 1));
    }

    /// <summary>
    /// 两连杆解算肘关节位置（大臂、小臂等长）；outwardRight 为 true 取靠右的解，否则取靠左的解。
    /// </summary>
    private static Point Elbow(Point shoulder, Point wrist, bool outwardRight)
    {
        Vector span = wrist - shoulder;
        double length = Math.Max(span.Length, 0.001);
        double distance = Math.Min(length, 2 * LinkLength - 0.5);
        Vector unit = span / length;
        double along = distance / 2;
        double height = Math.Sqrt(Math.Max(0, LinkLength * LinkLength - along * along));
        Point foot = shoulder + unit * along;
        var normal = new Vector(-unit.Y, unit.X);
        Point first = foot + normal * height;
        Point second = foot - normal * height;
        return outwardRight == (first.X > second.X) ? first : second;
    }

    #endregion

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly MotionTrack _rotation = new();
    private readonly MotionTrack _travel = new();
    private readonly Dictionary<RobotArmModel, MotionTrack> _armTracks = new();
    private readonly Dictionary<int, RobotArmModel> _placeholderArms = new();
    private readonly HashSet<RobotArmModel> _boundArms = new();
    private List<RobotArmModel> _arms = [];
    private bool _isRendering;

    public Robot()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SyncArms();
    }

    /// <summary>
    /// 各手臂这一帧的显示数据，Robot.xaml 里的手臂模板绑它；由控件自己维护，外部不要改。
    /// </summary>
    public ObservableCollection<ArmVisual> ArmVisuals { get; } = new();

    #region 依赖属性

    public RobotArmType ArmType
    {
        get => (RobotArmType)GetValue(ArmTypeProperty);
        set => SetValue(ArmTypeProperty, value);
    }

    public static readonly DependencyProperty ArmTypeProperty =
        DependencyProperty.Register(
            nameof(ArmType), typeof(RobotArmType), typeof(Robot),
            new PropertyMetadata(RobotArmType.Linear, OnSceneChanged));

    /// <summary>
    /// 转台目标朝向（俯视，屏幕上方为北）：North 0°、East 90°、South 180°、West 270°，只接受这四个方向；按最短路径转过去。
    /// </summary>
    public RobotDirection Rotation
    {
        get => (RobotDirection)GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    public static readonly DependencyProperty RotationProperty =
        DependencyProperty.Register(
            nameof(Rotation), typeof(RobotDirection), typeof(Robot),
            new PropertyMetadata(RobotDirection.North, OnPoseChanged),
            IsValidDirection);

    /// <summary>
    /// 水平平移目标：机械手中心相对控件中心向右移多少，负数向左；机械手可以移出控件范围。
    /// 按机械手设计尺寸 400×400 计，跟机械手一起按短边等比缩放：短边 400 时就是像素，短边 800 时实际移动 2 倍。
    /// </summary>
    public double Travel
    {
        get => (double)GetValue(TravelProperty);
        set => SetValue(TravelProperty, value);
    }

    public static readonly DependencyProperty TravelProperty =
        DependencyProperty.Register(
            nameof(Travel), typeof(double), typeof(Robot),
            new PropertyMetadata(0.0, OnPoseChanged),
            IsValidTravel);

    /// <summary>
    /// 伸出行程：手臂完全伸出（Extension = 1）时片心离转台中心多远，按机械手设计尺寸 400×400 计，跟机械手一起等比缩放。
    /// 各机台按自己的臂展配；形状不变，只是伸得远近不同。收回到底固定为 24。
    /// </summary>
    public double ExtendedReach
    {
        get => (double)GetValue(ExtendedReachProperty);
        set => SetValue(ExtendedReachProperty, value);
    }

    public static readonly DependencyProperty ExtendedReachProperty =
        DependencyProperty.Register(
            nameof(ExtendedReach), typeof(double), typeof(Robot),
            new PropertyMetadata(150.0, OnSceneChanged),
            IsValidReach);

    /// <summary>
    /// 手指（手臂）数量，1~4：控件画出 1..ArmCount 号手臂，Arms 里没有数据的手臂按空手收回显示。
    /// </summary>
    public int ArmCount
    {
        get => (int)GetValue(ArmCountProperty);
        set => SetValue(ArmCountProperty, value);
    }

    public static readonly DependencyProperty ArmCountProperty =
        DependencyProperty.Register(
            nameof(ArmCount), typeof(int), typeof(Robot),
            new FrameworkPropertyMetadata(2, OnArmCountChanged, CoerceArmCount));

    /// <summary>
    /// 手臂数据（RobotArmModel），按 Arm 号对应 1..ArmCount：超出 ArmCount 的忽略，同号取第一个；
    /// 集合增删与每只手臂的属性变化都会刷新。
    /// </summary>
    public IEnumerable? Arms
    {
        get => (IEnumerable?)GetValue(ArmsProperty);
        set => SetValue(ArmsProperty, value);
    }

    public static readonly DependencyProperty ArmsProperty =
        DependencyProperty.Register(
            nameof(Arms), typeof(IEnumerable), typeof(Robot),
            new PropertyMetadata(null, OnArmsChanged));

    /// <summary>
    /// 手臂上的片右键"建片"命令，命令参数为手臂号。
    /// </summary>
    public ICommand? CreateCommand
    {
        get => (ICommand?)GetValue(CreateCommandProperty);
        set => SetValue(CreateCommandProperty, value);
    }

    public static readonly DependencyProperty CreateCommandProperty =
        DependencyProperty.Register(
            nameof(CreateCommand), typeof(ICommand), typeof(Robot),
            new PropertyMetadata(null));

    /// <summary>
    /// 手臂上的片右键"删片"命令，命令参数为手臂号。
    /// </summary>
    public ICommand? DeleteCommand
    {
        get => (ICommand?)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public static readonly DependencyProperty DeleteCommandProperty =
        DependencyProperty.Register(
            nameof(DeleteCommand), typeof(ICommand), typeof(Robot),
            new PropertyMetadata(null));

    public bool CreateEnable
    {
        get => (bool)GetValue(CreateEnableProperty);
        set => SetValue(CreateEnableProperty, value);
    }

    public static readonly DependencyProperty CreateEnableProperty =
        DependencyProperty.Register(
            nameof(CreateEnable), typeof(bool), typeof(Robot),
            new PropertyMetadata(true));

    public bool DeleteEnable
    {
        get => (bool)GetValue(DeleteEnableProperty);
        set => SetValue(DeleteEnableProperty, value);
    }

    public static readonly DependencyProperty DeleteEnableProperty =
        DependencyProperty.Register(
            nameof(DeleteEnable), typeof(bool), typeof(Robot),
            new PropertyMetadata(true));

    /// <summary>
    /// 是否播放运动动画；false 时姿态直接跳到目标（设计器、截图用）。
    /// </summary>
    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    public static readonly DependencyProperty IsAnimationEnabledProperty =
        DependencyProperty.Register(
            nameof(IsAnimationEnabled), typeof(bool), typeof(Robot),
            new PropertyMetadata(true, OnPoseChanged));

    private static void OnSceneChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((Robot)dependencyObject).Redraw();
    }

    private static void OnPoseChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((Robot)dependencyObject).PlanMotion();
    }

    private static void OnArmCountChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((Robot)dependencyObject).SyncArms();
    }

    private static object CoerceArmCount(DependencyObject dependencyObject, object baseValue)
    {
        return Math.Clamp((int)baseValue, 1, MaxArmCount);
    }

    /// <summary>
    /// 朝向只认枚举里的四个方向，(RobotDirection)80 这类未定义的值直接拒绝。
    /// </summary>
    private static bool IsValidDirection(object value)
    {
        return Enum.IsDefined((RobotDirection)value);
    }

    private static bool IsValidTravel(object value)
    {
        return double.IsFinite((double)value);
    }

    /// <summary>
    /// 伸出行程至少要够把叉推出转台（不能比收回位置还近）。
    /// </summary>
    private static bool IsValidReach(object value)
    {
        return value is double reach && double.IsFinite(reach) && reach >= RetractedReach;
    }

    #endregion

    #region 数据订阅

    private static void OnArmsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var robot = (Robot)dependencyObject;
        if (args.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= robot.OnArmsCollectionChanged;
        }

        if (args.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += robot.OnArmsCollectionChanged;
        }

        robot.SyncArms();
    }

    private void OnArmsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        SyncArms();
    }

    /// <summary>
    /// 按 ArmCount 与 Arms 同步显示手臂：1..ArmCount 号有同号数据用数据，没有用空手占位；
    /// 新出现的手臂直接落到它当前的伸出量，不补播动画。
    /// </summary>
    private void SyncArms()
    {
        var bound = Arms?.OfType<RobotArmModel>().Distinct().ToList() ?? [];

        // 订阅全部数据手臂（含暂不显示的）：手臂号改到范围内时要能重新同步。
        foreach (var removed in _boundArms.Except(bound).ToList())
        {
            removed.PropertyChanged -= OnArmPropertyChanged;
            _boundArms.Remove(removed);
        }

        foreach (var arm in bound)
        {
            if (_boundArms.Add(arm))
            {
                arm.PropertyChanged += OnArmPropertyChanged;
            }
        }

        var arms = new List<RobotArmModel>(ArmCount);
        for (int number = 1; number <= ArmCount; number++)
        {
            arms.Add(bound.FirstOrDefault(arm => arm.Arm == number) ?? PlaceholderArm(number));
        }

        foreach (var removed in _armTracks.Keys.Except(arms).ToList())
        {
            _armTracks.Remove(removed);
        }

        foreach (var arm in arms)
        {
            if (!_armTracks.ContainsKey(arm))
            {
                _armTracks[arm] = new MotionTrack(arm.Extension);
            }
        }

        // 显示数据按手臂顺序对齐：同一只手臂沿用原来的那份，模板里的元素（含片控件）不重建。
        for (int index = 0; index < arms.Count; index++)
        {
            if (index >= ArmVisuals.Count)
            {
                ArmVisuals.Add(new ArmVisual(arms[index]));
            }
            else if (!ReferenceEquals(ArmVisuals[index].Model, arms[index]))
            {
                ArmVisuals[index] = new ArmVisual(arms[index]);
            }
        }

        while (ArmVisuals.Count > arms.Count)
        {
            ArmVisuals.RemoveAt(ArmVisuals.Count - 1);
        }

        _arms = arms;
        PlanMotion();
    }

    /// <summary>
    /// 没有数据的手臂号用空手占位；占位实例按号复用，轨道不重建。
    /// </summary>
    private RobotArmModel PlaceholderArm(int number)
    {
        if (!_placeholderArms.TryGetValue(number, out var arm))
        {
            arm = new RobotArmModel { Arm = number };
            _placeholderArms[number] = arm;
        }

        return arm;
    }

    private void OnArmPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RobotArmModel.Arm))
        {
            SyncArms();
            return;
        }

        // 超出 ArmCount 或被同号数据挤掉的手臂不显示，不用刷新。
        if (sender is not RobotArmModel arm || !_armTracks.ContainsKey(arm))
        {
            return;
        }

        if (args.PropertyName == nameof(RobotArmModel.Extension))
        {
            PlanMotion();
        }
        else
        {
            Redraw();
        }
    }

    #endregion

    #region 运动规划

    private double Now => _clock.Elapsed.TotalMilliseconds;

    /// <summary>
    /// 目标姿态变化后重排运动：① 收回 → ② 转向/平移 → ③ 伸出。
    /// 动画中途再改目标时，从当前显示姿态接着排，不跳变。
    /// </summary>
    private void PlanMotion()
    {
        double now = Now;
        AdvanceTracks(now);

        if (!IsLoaded || !IsAnimationEnabled)
        {
            JumpToTargets();
            PushFrame();
            UpdateRendering();
            return;
        }

        _rotation.Stop();
        _travel.Stop();
        foreach (var track in _armTracks.Values)
        {
            track.Stop();
        }

        // 枚举值就是度数。
        double rotationTarget = _rotation.Value + ShortestAngle(_rotation.Value, (int)Rotation);
        bool turn = Math.Abs(rotationTarget - _rotation.Value) > AngleEpsilon;
        bool shift = Math.Abs(Travel - _travel.Value) > TravelEpsilon;
        bool relocate = turn || shift;

        // ① 收回：要转向或平移时全部手臂先收回；原地不动时只收需要收的。
        double retracted = now;
        foreach (var arm in _arms)
        {
            var track = _armTracks[arm];
            double hold = relocate ? 0 : Math.Min(track.Value, arm.Extension);
            if (track.Value - hold > ExtensionEpsilon)
            {
                track.Append(hold, now, ExtendDurationMs);
                retracted = Math.Max(retracted, track.EndTime);
            }
        }

        // ② 转向与平移：手臂收完后同时进行。
        double arrived = retracted;
        if (turn)
        {
            _rotation.Append(rotationTarget, retracted, TurnDurationMs);
            arrived = Math.Max(arrived, _rotation.EndTime);
        }

        if (shift)
        {
            _travel.Append(Travel, retracted, TravelDurationMs);
            arrived = Math.Max(arrived, _travel.EndTime);
        }

        // ③ 伸出：到位且其它手臂收完后，再伸到目标伸出量。
        foreach (var arm in _arms)
        {
            var track = _armTracks[arm];
            if (arm.Extension - track.EndValue > ExtensionEpsilon)
            {
                track.Append(arm.Extension, arrived, ExtendDurationMs);
            }
        }

        PushFrame();
        UpdateRendering();
    }

    private void JumpToTargets()
    {
        _rotation.Jump((int)Rotation);
        _travel.Jump(Travel);
        foreach (var arm in _arms)
        {
            _armTracks[arm].Jump(arm.Extension);
        }
    }

    /// <summary>
    /// 推进全部轨道到指定时刻；返回本次是否有轨道在动（含本次刚走完的）。
    /// </summary>
    private bool AdvanceTracks(double now)
    {
        bool moving = _rotation.Advance(now);
        moving |= _travel.Advance(now);
        foreach (var track in _armTracks.Values)
        {
            moving |= track.Advance(now);
        }

        return moving;
    }

    private bool HasActiveTracks =>
        _rotation.IsActive || _travel.IsActive || _armTracks.Values.Any(track => track.IsActive);

    private static double ShortestAngle(double from, double to)
    {
        double delta = (to - from) % 360;
        if (delta > 180)
        {
            delta -= 360;
        }
        else if (delta < -180)
        {
            delta += 360;
        }

        return delta;
    }

    #endregion

    #region 渲染循环

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        // 首次显示（或切回页面）直接落到当前姿态，不从旧姿态补播动画。
        JumpToTargets();
        PushFrame();
        UpdateRendering();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        UpdateRendering();
    }

    private void Redraw()
    {
        AdvanceTracks(Now);
        PushFrame();
        UpdateRendering();
    }

    /// <summary>
    /// 有运动时挂到渲染帧上，停了就摘掉，不动时不占 CPU。
    /// </summary>
    private void UpdateRendering()
    {
        bool needed = IsLoaded && HasActiveTracks;
        if (needed == _isRendering)
        {
            return;
        }

        if (needed)
        {
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
        }

        _isRendering = needed;
    }

    private void OnRendering(object? sender, EventArgs args)
    {
        if (AdvanceTracks(Now))
        {
            PushFrame();
        }

        UpdateRendering();
    }

    /// <summary>
    /// 推一帧：把当前姿态写进 Robot.xaml 里的转台/平移 Transform 与各手臂的显示数据。
    /// </summary>
    private void PushFrame()
    {
        TurretRotation.Angle = _rotation.Value;
        TravelTransform.X = _travel.Value;

        bool frogLeg = ArmType == RobotArmType.FrogLeg;
        var extensions = _arms.Select(arm => _armTracks[arm].Value).ToList();
        bool anyExtended = extensions.Any(extension => extension > ExtendedThreshold);
        var layers = DrawOrder(extensions);

        for (int index = 0; index < _arms.Count && index < ArmVisuals.Count; index++)
        {
            var arm = _arms[index];
            double extension = extensions[index];
            bool dimmed = frogLeg && anyExtended && extension <= ExtendedThreshold;
            ArmVisuals[index].Update(
                extension,
                -Reach(extension),
                _rotation.Value + arm.Heading,
                StackOffset(_arms.Count, index, extension),
                layers[index],
                dimmed ? DimmedOpacity : 1,
                frogLeg);
        }
    }

    /// <summary>
    /// 叠放次序（每只手臂的层号，越大越靠上）：伸出的手臂在最上层；
    /// 收回的手臂里带片的压在空手之上，片不被叉挡住。
    /// </summary>
    private IReadOnlyList<int> DrawOrder(IReadOnlyList<double> extensions)
    {
        var ranked = Enumerable.Range(0, _arms.Count)
            .OrderBy(index => extensions[index] > ExtendedThreshold ? 1 : 0)
            .ThenBy(index => extensions[index])
            .ThenBy(index => _arms[index].Wafer is not null ? 1 : 0)
            .ThenBy(index => _arms[index].Arm)
            .ToList();

        var layers = new int[_arms.Count];
        for (int rank = 0; rank < ranked.Count; rank++)
        {
            layers[ranked[rank]] = rank + 1;
        }

        return layers;
    }

    #endregion

    /// <summary>
    /// 一只手臂这一帧的显示数据：Robot.xaml 里的手臂模板绑它。
    /// 数值都在设计尺寸（400×400）下、以转台中心为原点、正前方朝上，由控件每帧按伸出量算好写进来。
    /// </summary>
    public sealed class ArmVisual : ObservableObject
    {
        internal ArmVisual(RobotArmModel arm)
        {
            Model = arm;
        }

        /// <summary>
        /// 对应的手臂数据（页面传进来的那只手臂）。
        /// </summary>
        internal RobotArmModel Model { get; }

        private int _arm;

        /// <summary>手指号，写在滑座（或腕部）上。</summary>
        public int Arm
        {
            get => _arm;
            private set => SetProperty(ref _arm, value);
        }

        private WaferModel? _wafer;

        /// <summary>叉上的片；没片为 null，Wafer 控件自己隐藏。</summary>
        public WaferModel? Wafer
        {
            get => _wafer;
            private set => SetProperty(ref _wafer, value);
        }

        private bool _hasWafer;

        /// <summary>叉上有没有片：吸盘点亮用。</summary>
        public bool HasWafer
        {
            get => _hasWafer;
            private set => SetProperty(ref _hasWafer, value);
        }

        private bool _isExtended;

        /// <summary>是不是伸出去了：手臂号点亮用。</summary>
        public bool IsExtended
        {
            get => _isExtended;
            private set => SetProperty(ref _isExtended, value);
        }

        private double _angle;

        /// <summary>手臂朝向（转台角度 + 本手臂偏置），整只手臂绕转台中心转这么多。</summary>
        public double Angle
        {
            get => _angle;
            private set
            {
                if (SetProperty(ref _angle, value))
                {
                    CounterAngle = -value;
                }
            }
        }

        private double _counterAngle;

        /// <summary>反向角：手臂号与片上的文字靠它保持正立。</summary>
        public double CounterAngle
        {
            get => _counterAngle;
            private set => SetProperty(ref _counterAngle, value);
        }

        private double _stackOffset;

        /// <summary>收回时与其它手臂错开的偏移。</summary>
        public double StackOffset
        {
            get => _stackOffset;
            private set => SetProperty(ref _stackOffset, value);
        }

        private int _layer;

        /// <summary>叠放次序：伸出的手臂在最上层，收回的带片手臂压在空手之上。</summary>
        public int Layer
        {
            get => _layer;
            private set => SetProperty(ref _layer, value);
        }

        private double _dimOpacity = 1;

        /// <summary>蛙式互相交叠时，没干活的手臂压暗。</summary>
        public double DimOpacity
        {
            get => _dimOpacity;
            private set => SetProperty(ref _dimOpacity, value);
        }

        private double _waferTop;

        /// <summary>片心位置：叉、吸盘、滑座、腕部相对它是固定的，跟着它一起走。</summary>
        public double WaferTop
        {
            get => _waferTop;
            private set => SetProperty(ref _waferTop, value);
        }

        private Visibility _railVisibility = Visibility.Collapsed;

        /// <summary>导轨收进转台里就不画。</summary>
        public Visibility RailVisibility
        {
            get => _railVisibility;
            private set => SetProperty(ref _railVisibility, value);
        }

        private double _railOuterTop;

        public double RailOuterTop
        {
            get => _railOuterTop;
            private set => SetProperty(ref _railOuterTop, value);
        }

        private double _railOuterHeight;

        public double RailOuterHeight
        {
            get => _railOuterHeight;
            private set => SetProperty(ref _railOuterHeight, value);
        }

        private double _railInnerTop;

        public double RailInnerTop
        {
            get => _railInnerTop;
            private set => SetProperty(ref _railInnerTop, value);
        }

        private double _railInnerHeight;

        public double RailInnerHeight
        {
            get => _railInnerHeight;
            private set => SetProperty(ref _railInnerHeight, value);
        }

        private double _elbowLeftX;

        public double ElbowLeftX
        {
            get => _elbowLeftX;
            private set => SetProperty(ref _elbowLeftX, value);
        }

        private double _elbowLeftY;

        public double ElbowLeftY
        {
            get => _elbowLeftY;
            private set => SetProperty(ref _elbowLeftY, value);
        }

        private double _elbowRightX;

        public double ElbowRightX
        {
            get => _elbowRightX;
            private set => SetProperty(ref _elbowRightX, value);
        }

        private double _elbowRightY;

        public double ElbowRightY
        {
            get => _elbowRightY;
            private set => SetProperty(ref _elbowRightY, value);
        }

        private double _pivotY;

        /// <summary>蛙式腕部两个铰点的纵坐标（横坐标是 ±12）。</summary>
        public double PivotY
        {
            get => _pivotY;
            private set => SetProperty(ref _pivotY, value);
        }

        /// <summary>
        /// 按这一帧的伸出量与片心位置算出全部位置。
        /// </summary>
        internal void Update(double extension, double waferY, double angle, double stackOffset, int layer, double dimOpacity, bool frogLeg)
        {
            Arm = Model.Arm;
            Wafer = Model.Wafer;
            HasWafer = Model.Wafer is not null;
            IsExtended = extension > ExtendedThreshold;
            Angle = angle;
            StackOffset = stackOffset;
            Layer = layer;
            DimOpacity = dimOpacity;
            WaferTop = waferY;

            if (frogLeg)
            {
                double wristY = waferY + EffectorLength;
                PivotY = wristY;
                Point elbowLeft = Elbow(new Point(-ShoulderOffset, ShoulderY), new Point(-WristOffset, wristY), outwardRight: false);
                Point elbowRight = Elbow(new Point(ShoulderOffset, ShoulderY), new Point(WristOffset, wristY), outwardRight: true);
                ElbowLeftX = elbowLeft.X;
                ElbowLeftY = elbowLeft.Y;
                ElbowRightX = elbowRight.X;
                ElbowRightY = elbowRight.Y;
                RailVisibility = Visibility.Collapsed;
                return;
            }

            // 导轨从滑座后方伸到转台前沿，露出来才画；两节套筒各占一半。
            double railTop = waferY + CarriageToWafer + 10;
            if (railTop < TurretFront)
            {
                double middle = (railTop + TurretFront) / 2;
                RailOuterTop = middle;
                RailOuterHeight = TurretFront - middle + 4;
                RailInnerTop = railTop;
                RailInnerHeight = middle - railTop + 3;
                RailVisibility = Visibility.Visible;
            }
            else
            {
                RailVisibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// 单个运动量（转角、平移位置或某只手臂的伸出量）的分段缓动轨道。
    /// </summary>
    private sealed class MotionTrack
    {
        private readonly List<Segment> _segments = [];

        public MotionTrack(double value = 0)
        {
            Value = value;
        }

        /// <summary>
        /// 当前显示值。
        /// </summary>
        public double Value { get; private set; }

        public bool IsActive => _segments.Count > 0;

        /// <summary>
        /// 全部分段走完后的值。
        /// </summary>
        public double EndValue => _segments.Count > 0 ? _segments[^1].To : Value;

        /// <summary>
        /// 最后一段的结束时刻；没有分段时为负无穷。
        /// </summary>
        public double EndTime => _segments.Count > 0 ? _segments[^1].Start + _segments[^1].Duration : double.NegativeInfinity;

        public void Jump(double value)
        {
            _segments.Clear();
            Value = value;
        }

        /// <summary>
        /// 丢弃未走完的分段，停在当前显示值。
        /// </summary>
        public void Stop()
        {
            _segments.Clear();
        }

        public void Append(double to, double start, double duration)
        {
            _segments.Add(new Segment(EndValue, to, start, duration));
        }

        public bool Advance(double now)
        {
            if (_segments.Count == 0)
            {
                return false;
            }

            while (_segments.Count > 0)
            {
                var segment = _segments[0];
                if (now < segment.Start)
                {
                    Value = segment.From;
                    return true;
                }

                double progress = (now - segment.Start) / segment.Duration;
                if (progress < 1)
                {
                    Value = segment.From + (segment.To - segment.From) * EaseInOut(progress);
                    return true;
                }

                Value = segment.To;
                _segments.RemoveAt(0);
            }

            return true;
        }

        private static double EaseInOut(double progress)
        {
            return progress < 0.5
                ? 4 * progress * progress * progress
                : 1 - Math.Pow(-2 * progress + 2, 3) / 2;
        }

        private readonly record struct Segment(double From, double To, double Start, double Duration);
    }
}
