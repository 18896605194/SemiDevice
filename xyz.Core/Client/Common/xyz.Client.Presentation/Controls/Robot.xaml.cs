using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 机械手控件（俯视）：只有机械手本体，背景透明，由页面摆放。
/// 转台旋转、平移、各手臂独立伸缩，支持直伸直出与蛙式两种手臂结构；机械手随控件大小等比缩放；手臂上的片用 Wafer 控件。
/// 绑定只给目标姿态（Rotation / TravelX / TravelY / 各手臂 Extension），运动顺序由控件自己排：
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
    /// 没有运动、只有状态环动画（动作中/报警）时的刷新间隔，约 30 帧。
    /// </summary>
    private const double StatusFrameIntervalMs = 33;

    #endregion

    private readonly RobotBodyLayer _body = new();
    private readonly Dictionary<RobotArmModel, ArmVisual> _armVisuals = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly MotionTrack _rotation = new();
    private readonly MotionTrack _travelX = new();
    private readonly MotionTrack _travelY = new();
    private readonly Dictionary<RobotArmModel, MotionTrack> _armTracks = new();
    private readonly Dictionary<int, RobotArmModel> _placeholderArms = new();
    private readonly HashSet<RobotArmModel> _boundArms = new();
    private List<RobotArmModel> _arms = [];
    private bool _isRendering;
    private double _lastFrameMs = double.NegativeInfinity;

    public Robot()
    {
        InitializeComponent();
        StageHost.Children.Add(_body);
        StageHost.SizeChanged += (_, _) => PushFrame(Now);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SyncArms();
    }

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
    /// 底座状态环的显示状态。
    /// </summary>
    public RobotDisplayStatus Status
    {
        get => (RobotDisplayStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(
            nameof(Status), typeof(RobotDisplayStatus), typeof(Robot),
            new PropertyMetadata(RobotDisplayStatus.Offline, OnSceneChanged));

    /// <summary>
    /// 转台目标朝向（度）：0 = 正上方，顺时针为正；按最短路径转过去。
    /// </summary>
    public double Rotation
    {
        get => (double)GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    public static readonly DependencyProperty RotationProperty =
        DependencyProperty.Register(
            nameof(Rotation), typeof(double), typeof(Robot),
            new PropertyMetadata(0.0, OnPoseChanged));

    /// <summary>
    /// 水平平移目标（像素，相对控件中心，向右为正）；机械手可以移出控件范围。
    /// </summary>
    public double TravelX
    {
        get => (double)GetValue(TravelXProperty);
        set => SetValue(TravelXProperty, value);
    }

    public static readonly DependencyProperty TravelXProperty =
        DependencyProperty.Register(
            nameof(TravelX), typeof(double), typeof(Robot),
            new PropertyMetadata(0.0, OnPoseChanged));

    /// <summary>
    /// 垂直平移目标（像素，相对控件中心，向下为正）；机械手可以移出控件范围。
    /// </summary>
    public double TravelY
    {
        get => (double)GetValue(TravelYProperty);
        set => SetValue(TravelYProperty, value);
    }

    public static readonly DependencyProperty TravelYProperty =
        DependencyProperty.Register(
            nameof(TravelY), typeof(double), typeof(Robot),
            new PropertyMetadata(0.0, OnPoseChanged));

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
            StageHost.Children.Remove(_armVisuals[removed].Host);
            _armVisuals.Remove(removed);
        }

        foreach (var arm in arms)
        {
            if (!_armTracks.ContainsKey(arm))
            {
                _armTracks[arm] = new MotionTrack(arm.Extension);
                var visual = CreateArmVisual(arm);
                _armVisuals[arm] = visual;
                StageHost.Children.Add(visual.Host);
            }
        }

        _arms = arms;
        PlanMotion();
    }

    /// <summary>
    /// 一只手臂的显示元素：手臂绘制层 + 叉上的 Wafer 控件，装在同一个容器里，整体按叠放次序排层。
    /// </summary>
    private ArmVisual CreateArmVisual(RobotArmModel arm)
    {
        var wafer = new Wafer
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TranslateTransform(),
            Template = (ControlTemplate)FindResource("ArmWaferTemplate"),
            BorderThickness = new Thickness(2),
        };
        wafer.SetResourceReference(BorderBrushProperty, "DarkDisabledText");
        wafer.SetBinding(Wafer.DataProperty, new Binding(nameof(RobotArmModel.Wafer)) { Source = arm });
        wafer.SetBinding(Wafer.CommandParameterProperty, new Binding(nameof(RobotArmModel.Arm)) { Source = arm });
        wafer.SetBinding(Wafer.CreateCommandProperty, new Binding(nameof(CreateCommand)) { Source = this });
        wafer.SetBinding(Wafer.DeleteCommandProperty, new Binding(nameof(DeleteCommand)) { Source = this });
        wafer.SetBinding(Wafer.CreateEnableProperty, new Binding(nameof(CreateEnable)) { Source = this });
        wafer.SetBinding(Wafer.DeleteEnableProperty, new Binding(nameof(DeleteEnable)) { Source = this });

        var layer = new RobotArmLayer();
        var host = new Grid();
        host.Children.Add(layer);
        host.Children.Add(wafer);
        return new ArmVisual(host, layer, wafer);
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
            PushFrame(now);
            UpdateRendering();
            return;
        }

        _rotation.Stop();
        _travelX.Stop();
        _travelY.Stop();
        foreach (var track in _armTracks.Values)
        {
            track.Stop();
        }

        double rotationTarget = _rotation.Value + ShortestAngle(_rotation.Value, Rotation);
        bool turn = Math.Abs(rotationTarget - _rotation.Value) > AngleEpsilon;
        double travelDistance = Math.Sqrt(Math.Pow(TravelX - _travelX.Value, 2) + Math.Pow(TravelY - _travelY.Value, 2));
        bool shift = travelDistance > TravelEpsilon;
        bool relocate = turn || shift;

        // ① 收回：要转向或平移时全部手臂先收回；原地不动时只收需要收的。
        double retracted = now;
        foreach (var arm in _arms)
        {
            var track = _armTracks[arm];
            double hold = relocate ? 0 : Math.Min(track.Value, arm.Extension);
            if (track.Value - hold > ExtensionEpsilon)
            {
                track.Append(hold, now, ExtendDuration(track.Value - hold));
                retracted = Math.Max(retracted, track.EndTime);
            }
        }

        // ② 转向与平移：手臂收完后同时进行。
        double arrived = retracted;
        if (turn)
        {
            _rotation.Append(rotationTarget, retracted, TurnDuration(rotationTarget - _rotation.Value));
            arrived = Math.Max(arrived, _rotation.EndTime);
        }

        if (shift)
        {
            double duration = TravelDuration(travelDistance);
            _travelX.Append(TravelX, retracted, duration);
            _travelY.Append(TravelY, retracted, duration);
            arrived = Math.Max(arrived, _travelX.EndTime);
        }

        // ③ 伸出：到位且其它手臂收完后，再伸到目标伸出量。
        foreach (var arm in _arms)
        {
            var track = _armTracks[arm];
            if (arm.Extension - track.EndValue > ExtensionEpsilon)
            {
                track.Append(arm.Extension, arrived, ExtendDuration(arm.Extension - track.EndValue));
            }
        }

        PushFrame(now);
        UpdateRendering();
    }

    private void JumpToTargets()
    {
        _rotation.Jump(Rotation);
        _travelX.Jump(TravelX);
        _travelY.Jump(TravelY);
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
        moving |= _travelX.Advance(now);
        moving |= _travelY.Advance(now);
        foreach (var track in _armTracks.Values)
        {
            moving |= track.Advance(now);
        }

        return moving;
    }

    private bool HasActiveTracks =>
        _rotation.IsActive || _travelX.IsActive || _travelY.IsActive || _armTracks.Values.Any(track => track.IsActive);

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

    private static double TurnDuration(double degrees)
    {
        return 260 + 640 * Math.Min(Math.Abs(degrees), 180) / 180;
    }

    private static double TravelDuration(double distance)
    {
        return 260 + 540 * Math.Min(distance, 400) / 400;
    }

    private static double ExtendDuration(double distance)
    {
        return 220 + 480 * Math.Min(Math.Abs(distance), 1);
    }

    #endregion

    #region 渲染循环

    private bool IsStatusAnimated => Status is RobotDisplayStatus.Busy or RobotDisplayStatus.Alarm;

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        // 首次显示（或切回页面）直接落到当前姿态，不从旧姿态补播动画。
        JumpToTargets();
        PushFrame(Now);
        UpdateRendering();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        UpdateRendering();
    }

    private void Redraw()
    {
        double now = Now;
        AdvanceTracks(now);
        PushFrame(now);
        UpdateRendering();
    }

    /// <summary>
    /// 有运动或状态动画时挂到渲染帧上，都停了就摘掉，空闲时不占 CPU。
    /// </summary>
    private void UpdateRendering()
    {
        bool needed = IsLoaded && (HasActiveTracks || IsStatusAnimated);
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
        double now = Now;
        bool moving = AdvanceTracks(now);
        if (moving || now - _lastFrameMs >= StatusFrameIntervalMs)
        {
            PushFrame(now);
        }

        UpdateRendering();
    }

    /// <summary>
    /// 推一帧：本体层与各手臂层重画，手臂按叠放次序排层，片控件跟着叉走。
    /// </summary>
    private void PushFrame(double now)
    {
        _lastFrameMs = now;
        var arms = _arms
            .Select(arm => new RobotSceneArm(arm.Arm, _armTracks[arm].Value, arm.Heading, arm.Wafer is not null))
            .ToList();
        var frame = new RobotSceneFrame(ArmType, Status, _rotation.Value, _travelX.Value, _travelY.Value, arms, now);
        _body.Update(frame);

        var size = StageHost.RenderSize;
        double diameter = RobotDrawing.WaferRadius * 2 * RobotDrawing.Scale(size);
        var order = RobotDrawing.DrawOrder(frame);
        for (int rank = 0; rank < order.Count; rank++)
        {
            int index = order[rank];
            var visual = _armVisuals[_arms[index]];
            Panel.SetZIndex(visual.Host, rank + 1);
            visual.Host.Opacity = RobotDrawing.IsDimmed(frame, index) ? 0.45 : 1;
            visual.Layer.Update(frame, index);

            var wafer = visual.Wafer;
            if (wafer.Width != diameter)
            {
                wafer.Width = diameter;
                wafer.Height = diameter;
            }

            Point waferCenter = RobotDrawing.WaferCenter(size, frame, index);
            var offset = (TranslateTransform)wafer.RenderTransform;
            offset.X = waferCenter.X - diameter / 2;
            offset.Y = waferCenter.Y - diameter / 2;
        }
    }

    #endregion

    private sealed record ArmVisual(Grid Host, RobotArmLayer Layer, Wafer Wafer);

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
