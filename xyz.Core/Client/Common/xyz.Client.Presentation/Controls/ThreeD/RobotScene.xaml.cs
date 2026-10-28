using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>Robot 的三维显示场景；姿态由原 Robot 控件逐帧提供，不重复实现运动规划或连接设备。</summary>
public partial class RobotScene : UserControl
{
    private double _yaw = -18;
    private double _pitch = 42;
    private double _zoom = 10.4;
    private Point? _drag;

    public RobotScene()
    {
        InitializeComponent();
        Viewport.SizeChanged += (_, _) => UpdateCamera();
        UpdateCamera();
    }

    internal void SetPose(RobotArmType type, double rotation, double travel,
        IReadOnlyList<Robot.ArmVisual> arms, Geometry fork)
    {
        Body.SetPose(type, rotation, travel, arms, fork);
    }

    private void UpdateCamera()
    {
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(20 * Math.Cos(pitch) * Math.Sin(yaw),
            20 * Math.Sin(pitch), 20 * Math.Cos(pitch) * Math.Cos(yaw));
        Camera.Position = new Point3D(0, 0.55, -0.8) + offset;
        Camera.LookDirection = -offset;
        double aspect = Viewport.ActualHeight > 0 ? Viewport.ActualWidth / Viewport.ActualHeight : 1;
        Camera.Width = _zoom * Math.Max(1, aspect / 1.4);
    }

    private void OnDefaultView(object sender, RoutedEventArgs args)
    {
        _yaw = -18; _pitch = 42; _zoom = 10.4; UpdateCamera();
    }

    private void OnTopView(object sender, RoutedEventArgs args)
    {
        _yaw = 0; _pitch = 89; _zoom = 13.4; UpdateCamera();
    }

    private void OnDragStart(object sender, MouseButtonEventArgs args)
    {
        _drag = args.GetPosition(Viewport); Viewport.CaptureMouse(); args.Handled = true;
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs args)
    {
        _drag = null; Viewport.ReleaseMouseCapture();
    }

    private void OnDragLost(object sender, MouseEventArgs args) => _drag = null;

    private void OnDragMove(object sender, MouseEventArgs args)
    {
        if (_drag is not Point from) return;
        Point point = args.GetPosition(Viewport);
        _yaw -= (point.X - from.X) * 0.35;
        _pitch = Math.Clamp(_pitch + (point.Y - from.Y) * 0.25, 12, 89);
        _drag = point; UpdateCamera();
    }

    private void OnZoom(object sender, MouseWheelEventArgs args)
    {
        _zoom = Math.Clamp(_zoom - args.Delta / 120d * 0.5, 6, 24);
        UpdateCamera(); args.Handled = true;
    }
}
