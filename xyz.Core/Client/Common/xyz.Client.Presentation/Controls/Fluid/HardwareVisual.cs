using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Controls;

namespace xyz.Client.Presentation.Controls.Fluid;

/// <summary>公共流体硬件显示控件。所有状态和值由绑定提供，不发送设备命令。</summary>
public sealed class HardwareVisual : Grid
{
    private readonly FluidHardwareDrawing _drawing = new();
    private TankVisual3D? _tank;
    private ValveSymbol? _valve;
    private TextBlock? _reading;
    private ProcessHardwareScene? _process;
    private SignalLamp? _di;

    public FluidHardwareKind Kind { get => (FluidHardwareKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(FluidHardwareKind), typeof(HardwareVisual),
        new PropertyMetadata(FluidHardwareKind.FlowMeter, (d, _) => ((HardwareVisual)d).Rebuild()));
    public HardwareState State { get => (HardwareState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(HardwareState), typeof(HardwareVisual), new PropertyMetadata(HardwareState.Unknown, Changed));
    public double? Reading { get => (double?)GetValue(ReadingProperty); set => SetValue(ReadingProperty, value); }
    public static readonly DependencyProperty ReadingProperty = DependencyProperty.Register(nameof(Reading), typeof(double?), typeof(HardwareVisual), new PropertyMetadata(null, Changed), IsFinite);
    public double? Target { get => (double?)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target), typeof(double?), typeof(HardwareVisual), new PropertyMetadata(null, Changed), IsFinite);
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(HardwareVisual), new PropertyMetadata("", Changed));
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(HardwareVisual), new PropertyMetadata(false, Changed));
    internal bool AnimationRunning => _process?.AnimationRunning ?? _drawing.AnimationRunning;
    internal bool UsesSignalLamp => _di is not null;
    internal bool? DiActive => _di?.IsActive;
    internal bool UsesNativeTank => _tank is not null;
    internal bool UsesExistingValve => _valve is not null;
    internal string ReadingText => FormatReading(Reading, Unit, State);

    public HardwareVisual() { Background = Brushes.Transparent; Rebuild(); }
    private static bool IsFinite(object value) => value is null || value is double number && double.IsFinite(number);
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs _) => ((HardwareVisual)d).Apply();
    internal static string FormatReading(double? reading, string unit, HardwareState state) =>
        (state == HardwareState.Unknown || reading is null ? "—" : reading.Value.ToString("0.##", CultureInfo.InvariantCulture)) + (unit.Length > 0 ? " " + unit : "");
    private void Rebuild()
    {
        Children.Clear(); _tank = null; _valve = null; _reading = null; _process = null; _di = null;
        if (Kind == FluidHardwareKind.Tank)
        {
            var viewport = new Viewport3D { Margin = new Thickness(0, 0, 0, 24), IsHitTestVisible = false,
                Camera = new OrthographicCamera(new Point3D(0, 2.8, 8), new Vector3D(0, -1.5, -8), new Vector3D(0, 1, 0), 6.5) };
            var light = new Model3DGroup(); light.Children.Add(new AmbientLight(Color.FromRgb(145, 145, 145)));
            light.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -3, -2)));
            viewport.Children.Add(new ModelVisual3D { Content = light });
            _tank = new TankVisual3D { WallTransparency = 0.72 }; viewport.Children.Add(_tank); Children.Add(viewport);
            _reading = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            _reading.SetResourceReference(TextBlock.FontSizeProperty, "Size18"); Children.Add(_reading);
        }
        else if (Kind == FluidHardwareKind.OnOffValve)
        {
            _valve = new ValveSymbol { Width = 82, Height = 82, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Children.Add(_valve);
        }
        else if (Kind is FluidHardwareKind.CentrifugalPump or FluidHardwareKind.InlineHeater)
        {
            _process = new ProcessHardwareScene { Kind = Kind, Margin = new Thickness(0, 0, 0, 27) }; Children.Add(_process);
            _reading = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            _reading.SetResourceReference(TextBlock.FontSizeProperty, "Size16"); Children.Add(_reading);
        }
        else if (Kind == FluidHardwareKind.LeakSensor)
        {
            _di = new SignalLamp { Text = "Leak", IsAlarm = true };
            Children.Add(new Viewbox { Width = 130, Height = 32, Child = _di, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
        else Children.Add(_drawing);
        Apply();
    }
    private void Apply()
    {
        _drawing.Kind = Kind; _drawing.State = State; _drawing.Reading = Reading; _drawing.Target = Target; _drawing.Unit = Unit;
        _drawing.Refresh();
        if (_process is not null)
        {
            _process.State = State; _process.Reading = Reading; _process.Target = Target;
            _reading!.Text = ReadingText; _reading.SetResourceReference(TextBlock.ForegroundProperty, State == HardwareState.Fault ? "DarkAlarmStatus" : "DarkPrimaryText");
        }
        if (_di is not null) _di.IsActive = State == HardwareState.Unknown ? null : State == HardwareState.Fault;
        if (_tank is not null)
        {
            _tank.FillFraction = Reading.HasValue && State != HardwareState.Unknown ? Math.Clamp(Reading.Value / 100, 0, 1) : 0;
            _tank.IsAlarm = State == HardwareState.Fault; _tank.IsSelected = IsSelected;
            _reading!.Text = ReadingText; _reading.SetResourceReference(TextBlock.ForegroundProperty, State == HardwareState.Fault ? "DarkAlarmStatus" : "DarkPrimaryText");
        }
        if (_valve is not null)
        {
            _valve.IsOpen = State switch { HardwareState.Active => true, HardwareState.Stopped => false, _ => null };
            _valve.Opacity = State == HardwareState.Unknown ? 0.55 : 1;
            if (State == HardwareState.Fault) _valve.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(239, 83, 80), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 };
            else _valve.Effect = null;
        }
    }
}
