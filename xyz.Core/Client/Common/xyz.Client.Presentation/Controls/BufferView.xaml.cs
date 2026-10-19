using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 可直接放进 Grid 的独立缓存架视图。自带相机和灯光，继承页面 DataContext；
/// 没有模块标题、状态栏、计数文字或按钮。拖动旋转、滚轮缩放，不提供选片操作。
/// </summary>
public partial class BufferView : UserControl
{
    private const double DefaultYaw = 28;
    private const double DefaultPitch = 12;
    private readonly BufferVisual3D _rack = new();
    private Point3D _cameraTarget;
    private double _halfHeight = 1;
    private double _radius = 1.6;
    private double _yaw = DefaultYaw;
    private double _pitch = DefaultPitch;
    private double _zoom = 1;
    private Point? _pressedAt;
    private double _pressedYaw;
    private double _pressedPitch;
    private bool _dragged;

    public BufferView()
    {
        InitializeComponent();
        if (TryFindResource("DarkHardwareBodyColor") is Color body)
        {
            _rack.BodyColor = body;
        }

        if (TryFindResource("DarkAccent") is SolidColorBrush accent)
        {
            _rack.HighlightColor = accent.Color;
        }

        _rack.GeometryChanged += (_, _) => Refit();
        _rack.WaferCountChanged += (_, _) => UpdateCount();
        foreach (var property in new[] { FirstSlotProperty, SlotCountProperty, WafersProperty })
        {
            BindingOperations.SetBinding(_rack, property, new Binding(property.Name) { Source = this, Mode = BindingMode.OneWay });
        }

        Viewport.Children.Add(_rack);
        UpdateCount();
        Refit();
    }

    #region 依赖属性

    public int FirstSlot
    {
        get => (int)GetValue(FirstSlotProperty);
        set => SetValue(FirstSlotProperty, value);
    }

    public static readonly DependencyProperty FirstSlotProperty = BufferVisual3D.FirstSlotProperty.AddOwner(typeof(BufferView));

    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    public static readonly DependencyProperty SlotCountProperty = BufferVisual3D.SlotCountProperty.AddOwner(typeof(BufferView));

    public IEnumerable? Wafers
    {
        get => (IEnumerable?)GetValue(WafersProperty);
        set => SetValue(WafersProperty, value);
    }

    public static readonly DependencyProperty WafersProperty = BufferVisual3D.WafersProperty.AddOwner(typeof(BufferView));

    private static readonly DependencyPropertyKey WaferCountPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(WaferCount), typeof(int), typeof(BufferView), new PropertyMetadata(0));

    public static readonly DependencyProperty WaferCountProperty = WaferCountPropertyKey.DependencyProperty;

    public int WaferCount => (int)GetValue(WaferCountProperty);

    internal event EventHandler? WaferCountChanged;

    #endregion

    private void UpdateCount()
    {
        SetValue(WaferCountPropertyKey, _rack.WaferCount);
        WaferCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Refit()
    {
        Rect3D bounds = VisualTreeHelper.GetContentBounds(_rack);
        if (bounds.IsEmpty)
        {
            _cameraTarget = new Point3D();
            _halfHeight = 1;
            _radius = 1.6;
        }
        else
        {
            _cameraTarget = new Point3D(0, bounds.Y + bounds.SizeY / 2, 0);
            _radius = Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeZ * bounds.SizeZ) / 2;
            _halfHeight = bounds.SizeY / 2;
        }

        UpdateCamera();
    }

    /// <summary>恢复默认视角和缩放，不改变外部绑定的数据。</summary>
    public void ResetView() => SetView(DefaultYaw);

    /// <summary>转到对称的另一面并恢复默认缩放。</summary>
    public void ShowBackView() => SetView(DefaultYaw + 180);

    private void SetView(double yaw)
    {
        _yaw = yaw;
        _pitch = DefaultPitch;
        _zoom = 1;
        UpdateCamera();
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args) => UpdateCamera();

    private void UpdateCamera()
    {
        if (Camera is null || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            return;
        }

        double yaw = _yaw * Math.PI / 180;
        double pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(12 * Math.Cos(pitch) * Math.Sin(yaw), 12 * Math.Sin(pitch), 12 * Math.Cos(pitch) * Math.Cos(yaw));
        Camera.Position = _cameraTarget + offset;
        Camera.LookDirection = -offset;
        double aspect = Viewport.ActualWidth / Viewport.ActualHeight;
        // 按当前俯仰留空间：即使只有少量槽，转动时也不裁掉架子。
        double halfHeight = (_halfHeight * Math.Cos(pitch) + _radius * Math.Abs(Math.Sin(pitch))) * 1.2;
        Camera.Width = 2 * Math.Max(_radius * 1.12, halfHeight * aspect) / _zoom;
    }

    private void OnViewportMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        _pressedAt = args.GetPosition(Viewport);
        _pressedYaw = _yaw;
        _pressedPitch = _pitch;
        _dragged = false;
        Viewport.CaptureMouse();
        args.Handled = true;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs args)
    {
        if (_pressedAt is not Point start)
        {
            return;
        }

        Vector delta = args.GetPosition(Viewport) - start;
        _dragged |= delta.Length > 5;
        if (!_dragged)
        {
            return;
        }

        _yaw = (_pressedYaw - delta.X * 0.4) % 360;
        _pitch = Math.Clamp(_pressedPitch + delta.Y * 0.34, -11, 60);
        UpdateCamera();
    }

    private void OnViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        _pressedAt = null;
        Viewport.ReleaseMouseCapture();
        args.Handled = true;
    }

    private void OnViewportLostMouseCapture(object sender, MouseEventArgs args) => _pressedAt = null;

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs args)
    {
        _zoom = Math.Clamp(_zoom + args.Delta / 120.0 * 0.08, 0.7, 1.6);
        UpdateCamera();
        args.Handled = true;
    }
}
