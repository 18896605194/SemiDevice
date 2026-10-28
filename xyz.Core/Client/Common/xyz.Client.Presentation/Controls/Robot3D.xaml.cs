using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 可像二维 Robot 一样直接声明和绑定的三维机械手。直伸式、蛙式共用原动作轨道和手臂模型；
/// 自带相机、灯光、旋转和缩放，继承页面 DataContext，不连接服务或发设备指令。
/// </summary>
public partial class Robot3D : RobotViewBase
{
    private const double DefaultYaw = -18;
    private const double DefaultPitch = 42;
    private double _yaw = DefaultYaw;
    private double _pitch = DefaultPitch;
    private double _zoom = 1;
    private Point? _pressedAt;
    private double _pressedYaw;
    private double _pressedPitch;

    public Robot3D()
    {
        InitializeComponent();
        Viewport.SizeChanged += (_, _) => UpdateCamera();
        InitializeMotion();
        UpdateToolbar();
    }

    public bool ShowViewToolbar
    {
        get => (bool)GetValue(ShowViewToolbarProperty);
        set => SetValue(ShowViewToolbarProperty, value);
    }

    public static readonly DependencyProperty ShowViewToolbarProperty = DependencyProperty.Register(
        nameof(ShowViewToolbar), typeof(bool), typeof(Robot3D),
        new PropertyMetadata(true, (sender, _) => ((Robot3D)sender).UpdateToolbar()));

    protected override void RenderFrame(double rotation, double travel)
    {
        Body.SetPose(ArmType, rotation, travel, ArmVisuals);
        UpdateCamera();
    }

    /// <summary>恢复默认观察角度和缩放，不改变设备状态或外部绑定。</summary>
    public void ResetView()
    {
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _zoom = 1;
        UpdateCamera();
    }

    /// <summary>俯视原型平面；保留原 2D 的北、东、南、西方向。</summary>
    public void ShowTopView()
    {
        _yaw = 0;
        _pitch = 89;
        _zoom = 1;
        UpdateCamera();
    }

    private void UpdateToolbar()
    {
        if (ViewToolbar is not null) ViewToolbar.Visibility = ShowViewToolbar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCamera()
    {
        if (Camera is null) return;
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        double radius = Math.Max(DesignSize / 2, ExtendedReach + WaferDiameter / 2) * RobotVisual3D.UnitsPerPixel
            + Math.Abs(Travel) * RobotVisual3D.UnitsPerPixel;
        double distance = Math.Max(20, radius * 4);
        var offset = new Vector3D(distance * Math.Cos(pitch) * Math.Sin(yaw),
            distance * Math.Sin(pitch), distance * Math.Cos(pitch) * Math.Cos(yaw));
        Camera.Position = new Point3D(0, 0.7, 0) + offset;
        Camera.LookDirection = -offset;
        Camera.FarPlaneDistance = distance * 4;
        double aspect = Viewport.ActualHeight > 0 ? Viewport.ActualWidth / Viewport.ActualHeight : 1;
        Camera.Width = radius * 2.1 * Math.Max(1, aspect) / _zoom;
    }

    private void OnDefaultView(object sender, RoutedEventArgs args) => ResetView();
    private void OnTopView(object sender, RoutedEventArgs args) => ShowTopView();

    private void OnDragStart(object sender, MouseButtonEventArgs args)
    {
        _pressedAt = args.GetPosition(Scene);
        _pressedYaw = _yaw;
        _pressedPitch = _pitch;
        Scene.CaptureMouse();
        args.Handled = true;
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs args)
    {
        _pressedAt = null;
        Scene.ReleaseMouseCapture();
        args.Handled = true;
    }

    private void OnDragLost(object sender, MouseEventArgs args) => _pressedAt = null;

    private void OnDragMove(object sender, MouseEventArgs args)
    {
        if (_pressedAt is not Point start) return;
        Vector delta = args.GetPosition(Scene) - start;
        _yaw = (_pressedYaw - delta.X * 0.35) % 360;
        _pitch = Math.Clamp(_pressedPitch + delta.Y * 0.25, 12, 89);
        UpdateCamera();
    }

    private void OnZoom(object sender, MouseWheelEventArgs args)
    {
        _zoom = Math.Clamp(_zoom + args.Delta / 120d * 0.08, 0.5, 2.5);
        UpdateCamera();
        args.Handled = true;
    }
}
