using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using xyz.Client.Presentation.Controls.ThreeD;

namespace xyz.Client.Presentation.Controls.Fluid;

/// <summary>泵/加热器的二维正视和交互式三维显示，使用平台硬件基类；视图隐藏时停帧。</summary>
public sealed class ProcessHardwareScene : Grid
{
    private readonly Viewport3D _viewport = new() { IsHitTestVisible = false };
    private readonly OrthographicCamera _camera = new() { UpDirection = new Vector3D(0, 1, 0) };
    private readonly DispatcherTimer _timer;
    private HardwareVisual3D? _hardware;
    private Point? _drag;
    private double _yaw = -28, _pitch = 17, _zoom = 1;
    private long _lastFrame;

    public FluidHardwareKind Kind { get => (FluidHardwareKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(FluidHardwareKind), typeof(ProcessHardwareScene),
        new PropertyMetadata(FluidHardwareKind.CentrifugalPump, (d, _) => ((ProcessHardwareScene)d).Rebuild()), value => value is FluidHardwareKind.CentrifugalPump or FluidHardwareKind.InlineHeater);
    public HardwareState State { get => (HardwareState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(HardwareState), typeof(ProcessHardwareScene), new PropertyMetadata(HardwareState.Stopped, Changed));
    public double? Reading { get => (double?)GetValue(ReadingProperty); set => SetValue(ReadingProperty, value); }
    public static readonly DependencyProperty ReadingProperty = DependencyProperty.Register(nameof(Reading), typeof(double?), typeof(ProcessHardwareScene), new PropertyMetadata(null, Changed));
    public double? Target { get => (double?)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target), typeof(double?), typeof(ProcessHardwareScene), new PropertyMetadata(null, Changed));
    public double PowerFraction { get => (double)GetValue(PowerFractionProperty); set => SetValue(PowerFractionProperty, value); }
    public static readonly DependencyProperty PowerFractionProperty = DependencyProperty.Register(nameof(PowerFraction), typeof(double), typeof(ProcessHardwareScene), new PropertyMetadata(0.65d, Changed), value => value is double n && double.IsFinite(n) && n is >= 0 and <= 1);
    public bool ShowInternals { get => (bool)GetValue(ShowInternalsProperty); set => SetValue(ShowInternalsProperty, value); }
    public static readonly DependencyProperty ShowInternalsProperty = DependencyProperty.Register(nameof(ShowInternals), typeof(bool), typeof(ProcessHardwareScene), new PropertyMetadata(true, Changed));
    public bool IsInteractive { get => (bool)GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }
    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(nameof(IsInteractive), typeof(bool), typeof(ProcessHardwareScene), new PropertyMetadata(false));
    public HardwareViewMode ViewMode { get => (HardwareViewMode)GetValue(ViewModeProperty); set => SetValue(ViewModeProperty, value); }
    public static readonly DependencyProperty ViewModeProperty = DependencyProperty.Register(nameof(ViewMode), typeof(HardwareViewMode), typeof(ProcessHardwareScene),
        new PropertyMetadata(HardwareViewMode.ThreeD, (d, _) => ((ProcessHardwareScene)d).UpdateCamera()));
    internal bool AnimationRunning => _timer.IsEnabled;
    internal HardwareVisual3D Hardware => _hardware!;
    internal double Yaw => _yaw;
    internal double ZoomLevel => _zoom;
    internal OrthographicCamera Camera => _camera;

    public ProcessHardwareScene()
    {
        Background = Brushes.Transparent; ClipToBounds = true; Focusable = true;
        _viewport.Camera = _camera; Children.Add(_viewport);
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp(); double elapsed = Math.Min(0.1, (double)(now - _lastFrame) / Stopwatch.Frequency); _lastFrame = now;
            if (_hardware is PumpPreviewVisual3D pump) pump.Advance(elapsed);
            if (_hardware is HeaterPreviewVisual3D heater) heater.Advance(elapsed);
        };
        IsVisibleChanged += (_, _) => UpdateAnimation();
        SizeChanged += (_, _) => UpdateCamera();
        MouseLeftButtonDown += (_, e) =>
        {
            if (!IsInteractive || ViewMode == HardwareViewMode.Front2D) return;
            Focus(); if (e.ClickCount == 2) ResetCamera(); else { CaptureMouse(); _drag = e.GetPosition(this); } e.Handled = true;
        };
        MouseMove += (_, e) => { if (_drag is not { } previous || e.LeftButton != MouseButtonState.Pressed) return; var next = e.GetPosition(this); RotateView(next - previous); _drag = next; };
        MouseLeftButtonUp += (_, e) => { if (_drag is null) return; _drag = null; ReleaseMouseCapture(); e.Handled = true; };
        LostMouseCapture += (_, _) => _drag = null;
        MouseWheel += (_, e) => { if (!IsInteractive || ViewMode == HardwareViewMode.Front2D) return; ZoomView(e.Delta); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (!IsInteractive || ViewMode == HardwareViewMode.Front2D) return;
            Vector delta = e.Key switch { Key.Left => new(-10, 0), Key.Right => new(10, 0), Key.Up => new(0, -10), Key.Down => new(0, 10), _ => default };
            if (delta == default) return; RotateView(delta); e.Handled = true;
        };
        Rebuild();
    }
    private static Color ColorOf(string key, Color fallback) => Application.Current?.TryFindResource(key) switch { Color c => c, SolidColorBrush b => b.Color, _ => fallback };
    private void Rebuild()
    {
        _viewport.Children.Clear();
        var lights = new Model3DGroup(); lights.Children.Add(new AmbientLight(ColorOf("DarkHardwareAmbientLightColor", Color.FromRgb(112, 112, 112))));
        lights.Children.Add(new DirectionalLight(ColorOf("DarkHardwareKeyLightColor", Colors.LightGray), new Vector3D(-1, -3, -2)));
        lights.Children.Add(new DirectionalLight(ColorOf("DarkHardwareFillLightColor", Colors.DimGray), new Vector3D(2, -1, 1)));
        _viewport.Children.Add(new ModelVisual3D { Content = lights });
        _hardware = Kind == FluidHardwareKind.InlineHeater ? new HeaterPreviewVisual3D() : new PumpPreviewVisual3D();
        _hardware.BodyColor = ColorOf("DarkHardwareBodyColor", Color.FromRgb(104, 104, 104));
        _hardware.HighlightColor = ColorOf("DarkAccent", Color.FromRgb(66, 165, 245));
        _viewport.Children.Add(_hardware); Apply(); UpdateCamera();
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs _) => ((ProcessHardwareScene)d).Apply();
    private void Apply()
    {
        if (_hardware is null) return;
        _hardware.IsError = State == HardwareState.Fault;
        _viewport.Opacity = State == HardwareState.Unknown ? 0.5 : 1;
        if (_hardware is PumpPreviewVisual3D pump)
        {
            pump.IsRunning = State == HardwareState.Active; pump.Speed = Math.Clamp(Reading ?? 0, 0, 10000); pump.ShowInternals = ShowInternals; pump.RefreshFeedback();
        }
        if (_hardware is HeaterPreviewVisual3D heater)
        {
            heater.IsHeating = State == HardwareState.Active; heater.Temperature = Reading; heater.TargetTemperature = Target;
            heater.PowerFraction = PowerFraction; heater.ShowInternals = ShowInternals; heater.RefreshFeedback();
        }
        UpdateAnimation();
    }
    private void UpdateAnimation()
    {
        bool active = IsVisible && State == HardwareState.Active && (Kind == FluidHardwareKind.CentrifugalPump ? Reading > 0 : PowerFraction > 0);
        if (active && !_timer.IsEnabled) { _lastFrame = Stopwatch.GetTimestamp(); _timer.Start(); }
        else if (!active) _timer.Stop();
    }
    private void UpdateCamera()
    {
        if (ViewMode == HardwareViewMode.Front2D)
        {
            var bounds = ProcessHardwareProjection.Bounds(Kind);
            // 保持管口与二维布局坐标一致，宽高调整直接映射到同一模型的XY投影。
            double sy = ActualWidth > 0 && ActualHeight > 0 ? bounds.Width * ActualHeight / ActualWidth / bounds.Height : 1;
            if (_hardware is not null) _hardware.Transform = new ScaleTransform3D(1, sy, 1);
            _camera.Position = new Point3D(bounds.X + bounds.Width / 2, (bounds.Y + bounds.Height / 2) * sy, 10);
            _camera.LookDirection = new Vector3D(0, 0, -10); _camera.Width = bounds.Width;
            return;
        }
        if (_hardware is not null) _hardware.Transform = Transform3D.Identity;
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var target = new Point3D(-0.1, 1.0, 0);
        var offset = new Vector3D(Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch)) * 9;
        _camera.Position = target + offset; _camera.LookDirection = -offset;
        _camera.Width = Math.Max(5.2, 3.0 * ActualWidth / Math.Max(1, ActualHeight)) / _zoom;
    }
    internal void RotateView(Vector delta) { _yaw += delta.X * 0.4; _pitch = Math.Clamp(_pitch - delta.Y * 0.4, 4, 70); UpdateCamera(); }
    internal void ZoomView(int delta) { _zoom = Math.Clamp(_zoom * Math.Pow(1.12, delta / 120.0), 0.6, 2.0); UpdateCamera(); }
    public void ResetCamera() { _yaw = -28; _pitch = 17; _zoom = 1; UpdateCamera(); }
    public void FrontCamera() { _yaw = 0; _pitch = 0; _zoom = 1; UpdateCamera(); }
}
