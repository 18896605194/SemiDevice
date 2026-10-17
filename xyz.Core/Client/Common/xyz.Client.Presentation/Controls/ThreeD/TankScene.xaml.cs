using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 供液槽三维图：液面跟液位，信号灯跟各自的 DI 信号
/// </summary>
public partial class TankScene : UserControl
{
    #region 视角

    private const double DefaultYaw = -18;
    private const double DefaultPitch = 16;
    private const double FrontYaw = 0;
    private const double FrontPitch = 4.6;
    private const double MinPitch = 4;
    private const double MaxPitch = 40;
    private const double MinZoom = 0.72;
    private const double MaxZoom = 1.08;
    private const double DragYawPerPixel = 0.35;
    private const double DragPitchPerPixel = 0.25;
    private const double WheelNotch = 120;
    private const double ZoomPerNotch = 0.06;
    private const double CameraDistance = 10;

    /// <summary>
    /// 取景时左右、上下留的像素（右边放信号灯）
    /// </summary>
    private const double SideRoom = 132;

    private const double VerticalRoom = 64;

    /// <summary>
    /// 罐往左让多少像素
    /// </summary>
    private const double LampShift = 48;

    #endregion

    #region 信号灯和液面

    /// <summary>
    /// 信号灯贴在罐壁外多远（罐半径的倍数）
    /// </summary>
    private const double LampRadius = 1.015;

    /// <summary>
    /// 各信号在罐高上的位置（示意）
    /// </summary>
    private const double LowLowHeight = 0.12;

    private const double LowHeight = 0.29;
    private const double HighHeight = 0.65;
    private const double HighHighHeight = 0.79;
    private const double OverfillHeight = 0.94;

    /// <summary>
    /// 换液位时液面过渡多久
    /// </summary>
    private const double FillMilliseconds = 200;

    #endregion

    private double _yaw = DefaultYaw;
    private double _pitch = DefaultPitch;
    private double _zoom = 1;
    private double _scale = 1;
    private Point3D _target;
    private Point? _dragFrom;

    public TankScene()
    {
        InitializeComponent();
        Body.WallTransparency = WallTransparency;
        Stage.SizeChanged += (_, _) => UpdateCamera();
        IsVisibleChanged += OnVisibleChanged;

        // 灯从不画到画出来（第一次收到推送）才有高度，那时再摆一次位置
        foreach (var lamp in new[] { LowLowSignal, LowSignal, HighSignal, HighHighSignal, OverfillSignal })
        {
            lamp.SizeChanged += (_, _) => UpdateCamera();
        }

        BindSignals();
        ApplyLevel(false);
    }

    #region 依赖属性

    /// <summary>
    /// 槽的显示模型（后端 TankDto）
    /// </summary>
    public TankModel? Tank
    {
        get => (TankModel?)GetValue(TankProperty);
        set => SetValue(TankProperty, value);
    }

    public static readonly DependencyProperty TankProperty = DependencyProperty.Register(
        nameof(Tank), typeof(TankModel), typeof(TankScene), new PropertyMetadata(null, OnTankChanged));

    /// <summary>
    /// 罐壁透明度（0～1），外部按需要调
    /// </summary>
    public double WallTransparency
    {
        get => (double)GetValue(WallTransparencyProperty);
        set => SetValue(WallTransparencyProperty, value);
    }

    public static readonly DependencyProperty WallTransparencyProperty = DependencyProperty.Register(
        nameof(WallTransparency), typeof(double), typeof(TankScene),
        new PropertyMetadata(0.72, OnWallTransparencyChanged), IsFraction);

    /// <summary>
    /// 换了一个槽：灯改绑新模型，液面直接落位
    /// </summary>
    private static void OnTankChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var scene = (TankScene)sender;
        if (args.OldValue is TankModel old)
        {
            old.PropertyChanged -= scene.OnTankPropertyChanged;
        }

        if (args.NewValue is TankModel tank)
        {
            tank.PropertyChanged += scene.OnTankPropertyChanged;
        }

        scene.BindSignals();
        scene.ApplyLevel(false);
    }

    private void OnTankPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TankModel.Level))
        {
            ApplyLevel(true);
        }
    }

    private static void OnWallTransparencyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var scene = (TankScene)sender;
        scene.Body.WallTransparency = scene.WallTransparency;
    }

    private static bool IsFraction(object value)
    {
        return value is double fraction && double.IsFinite(fraction) && fraction >= 0 && fraction <= 1;
    }

    #endregion

    #region 信号灯

    /// <summary>
    /// 五盏灯各绑一个信号；没有槽就都不画
    /// </summary>
    private void BindSignals()
    {
        var tank = Tank;
        BindSignal(LowLowSignal, tank?.LL);
        BindSignal(LowSignal, tank?.L);
        BindSignal(HighSignal, tank?.H);
        BindSignal(HighHighSignal, tank?.HH);
        BindSignal(OverfillSignal, tank?.Overfill);
    }

    /// <summary>
    /// 灯的亮灭、名字跟这个信号走，sc 里没配就不画
    /// </summary>
    private static void BindSignal(SignalLamp lamp, DiSensorModel? signal)
    {
        if (signal is null)
        {
            BindingOperations.ClearBinding(lamp, SignalLamp.IsActiveProperty);
            BindingOperations.ClearBinding(lamp, SignalLamp.TextProperty);
            BindingOperations.ClearBinding(lamp, VisibilityProperty);
            lamp.Visibility = Visibility.Collapsed;
            return;
        }

        BindingOperations.SetBinding(lamp, SignalLamp.IsActiveProperty,
            new Binding(nameof(DiSensorModel.IsActive)) { Source = signal, Mode = BindingMode.OneWay });
        BindingOperations.SetBinding(lamp, SignalLamp.TextProperty,
            new Binding(nameof(DiSensorModel.Name)) { Source = signal, Mode = BindingMode.OneWay });
        BindingOperations.SetBinding(lamp, VisibilityProperty,
            new Binding(nameof(DiSensorModel.IsPresent)) { Source = signal, Mode = BindingMode.OneWay, Converter = PresenceConverter });
    }

    private static readonly BooleanToVisibilityConverter PresenceConverter = new();

    #endregion

    #region 液位

    /// <summary>
    /// 液面、液体颜色、右上角的字一起换；显示着才过渡
    /// </summary>
    private void ApplyLevel(bool animate)
    {
        TankLevel level = TankLevel.Unknown;
        var tank = Tank;
        if (tank is not null)
        {
            level = tank.Level;
        }

        double from = Body.FillFraction;
        double to = FillOf(level);
        Body.BeginAnimation(TankVisual3D.FillFractionProperty, null);
        Body.FillFraction = to;
        Body.IsAlarm = level == TankLevel.HH || level == TankLevel.Overfill;
        if (animate && IsVisible && level != TankLevel.Unknown)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(FillMilliseconds))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Body.BeginAnimation(TankVisual3D.FillFractionProperty, animation);
        }

        LevelDot.Tag = level;
        LevelText.Tag = level;
    }

    /// <summary>
    /// 液面画多高：落在上下两个信号点中间，只是档位示意
    /// </summary>
    private static double FillOf(TankLevel level)
    {
        switch (level)
        {
            case TankLevel.LL:
                return 0.05;
            case TankLevel.L:
                return 0.205;
            case TankLevel.Normal:
                return 0.47;
            case TankLevel.H:
                return 0.72;
            case TankLevel.HH:
                return 0.865;
            case TankLevel.Overfill:
                return 0.998;
            default:
                return 0;
        }
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!IsVisible)
        {
            Body.BeginAnimation(TankVisual3D.FillFractionProperty, null);
        }
    }

    #endregion

    #region 视角

    /// <summary>
    /// 按图的大小取景：罐整个放得下、右边留出信号灯，再把信号灯摆到罐的右边沿外
    /// </summary>
    private void UpdateCamera()
    {
        double width = Stage.ActualWidth;
        double height = Stage.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double yaw = _yaw * Math.PI / 180;
        double pitch = _pitch * Math.PI / 180;
        double diameter = 2 * TankVisual3D.RimRadius;
        double projectedHeight = TankVisual3D.TankHeight * Math.Cos(pitch) + diameter * Math.Sin(pitch);
        _scale = Math.Max(1, Math.Min((width - SideRoom) / diameter, (height - VerticalRoom) / projectedHeight) * _zoom);
        var right = new Vector3D(Math.Cos(yaw), 0, -Math.Sin(yaw));
        _target = new Point3D(0, TankVisual3D.TankHeight / 2, 0) + right * (LampShift / _scale);
        var offset = new Vector3D(Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch)) * CameraDistance;
        Camera.Position = _target + offset;
        Camera.LookDirection = -offset;
        Camera.Width = width / _scale;
        Body.Azimuth = _yaw;

        PlaceLamp(LowLowSignal, LowLowHeight, right);
        PlaceLamp(LowSignal, LowHeight, right);
        PlaceLamp(HighSignal, HighHeight, right);
        PlaceLamp(HighHighSignal, HighHighHeight, right);
        PlaceLamp(OverfillSignal, OverfillHeight, right);
    }

    /// <summary>
    /// 灯放在屏幕上罐的右边沿外，转到哪个角度都不压在液体上
    /// </summary>
    private void PlaceLamp(FrameworkElement row, double heightShare, Vector3D right)
    {
        Point screen = Project(new Point3D(right.X * LampRadius, TankVisual3D.TankHeight * heightShare, right.Z * LampRadius));
        Canvas.SetLeft(row, screen.X);
        Canvas.SetTop(row, screen.Y - row.ActualHeight / 2);
    }

    /// <summary>
    /// 三维点投到图上的位置（正交相机，图中心对着观察点）
    /// </summary>
    private Point Project(Point3D point)
    {
        Vector3D forward = Camera.LookDirection;
        forward.Normalize();
        Vector3D right = Vector3D.CrossProduct(forward, Camera.UpDirection);
        right.Normalize();
        Vector3D up = Vector3D.CrossProduct(right, forward);
        up.Normalize();
        Vector3D delta = point - _target;
        return new Point(Stage.ActualWidth / 2 + Vector3D.DotProduct(delta, right) * _scale,
            Stage.ActualHeight / 2 - Vector3D.DotProduct(delta, up) * _scale);
    }

    private void SetView(double yaw, double pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
        _zoom = 1;
        UpdateCamera();
    }

    private void OnDefaultViewClick(object sender, RoutedEventArgs args)
    {
        SetView(DefaultYaw, DefaultPitch);
    }

    private void OnFrontViewClick(object sender, RoutedEventArgs args)
    {
        SetView(FrontYaw, FrontPitch);
    }

    private void OnStageMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (Stage.CaptureMouse())
        {
            _dragFrom = args.GetPosition(Stage);
            args.Handled = true;
        }
    }

    private void OnStageMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        _dragFrom = null;
        Stage.ReleaseMouseCapture();
    }

    private void OnStageLostMouseCapture(object sender, MouseEventArgs args)
    {
        _dragFrom = null;
    }

    private void OnStageMouseMove(object sender, MouseEventArgs args)
    {
        var from = _dragFrom;
        if (from is null)
        {
            return;
        }

        Point current = args.GetPosition(Stage);
        _yaw = (_yaw + (current.X - from.Value.X) * DragYawPerPixel) % 360;
        _pitch = Math.Clamp(_pitch + (current.Y - from.Value.Y) * DragPitchPerPixel, MinPitch, MaxPitch);
        _dragFrom = current;
        UpdateCamera();
    }

    private void OnStageMouseWheel(object sender, MouseWheelEventArgs args)
    {
        _zoom = Math.Clamp(_zoom + args.Delta / WheelNotch * ZoomPerNotch, MinZoom, MaxZoom);
        UpdateCamera();
        args.Handled = true;
    }

    #endregion
}
