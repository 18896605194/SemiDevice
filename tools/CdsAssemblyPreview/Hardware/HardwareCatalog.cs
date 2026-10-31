using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using xyz.Client.Presentation.Localization;

namespace CdsAssemblyPreview;

public enum HardwareRole { Actuator, Valve, Monitor, Passive }

public sealed record HardwareDefinition(FluidHardwareKind Kind, string Group, HardwareRole Role, string Tag, string DefaultUnit,
    double DefaultValue, double Minimum, double Maximum, bool HasTarget = false)
{
    public string Title => L10n.Get("hw.kind." + Kind);
    public string Description => L10n.Get("hw.about." + Kind);
    public string GroupTitle => L10n.Get("hw.group." + Group);
    public bool HasReading => DefaultUnit.Length > 0;
}
public sealed record HardwareGroup(string Key)
{
    public string Title => L10n.Get("hw.group." + Key);
}

public static class HardwareCatalog
{
    public static IReadOnlyList<HardwareDefinition> Create() =>
    [
        new(FluidHardwareKind.Tank, "delivery", HardwareRole.Monitor, "TK", "%", 55, 0, 100),
        new(FluidHardwareKind.CentrifugalPump, "delivery", HardwareRole.Actuator, "P", "rpm", 1450, 0, 10000, true),
        new(FluidHardwareKind.DiaphragmPump, "delivery", HardwareRole.Actuator, "DP", "cycle/min", 60, 0, 500, true),
        new(FluidHardwareKind.MeteringPump, "delivery", HardwareRole.Actuator, "MP", "mL/min", 30, 0, 1000, true),
        new(FluidHardwareKind.Mixer, "delivery", HardwareRole.Actuator, "AG", "rpm", 300, 0, 5000, true),
        new(FluidHardwareKind.InlineHeater, "thermal", HardwareRole.Actuator, "HT", "°C", 65, -50, 300, true),
        new(FluidHardwareKind.ImmersionHeater, "thermal", HardwareRole.Actuator, "IH", "°C", 55, -50, 300, true),
        new(FluidHardwareKind.HeatExchanger, "thermal", HardwareRole.Passive, "HX", "°C", 22, -50, 300),
        new(FluidHardwareKind.OnOffValve, "valves", HardwareRole.Valve, "XV", "", 0, 0, 100),
        new(FluidHardwareKind.ManualValve, "valves", HardwareRole.Valve, "HV", "", 0, 0, 100),
        new(FluidHardwareKind.ProportionalValve, "valves", HardwareRole.Valve, "CV", "%", 45, 0, 100, true),
        new(FluidHardwareKind.ThreeWayValve, "valves", HardwareRole.Valve, "3V", "", 0, 0, 100),
        new(FluidHardwareKind.CheckValve, "valves", HardwareRole.Passive, "NRV", "", 0, 0, 100),
        new(FluidHardwareKind.PressureRegulator, "valves", HardwareRole.Passive, "PR", "MPa", 0.3, 0, 2, true),
        new(FluidHardwareKind.ReliefValve, "valves", HardwareRole.Passive, "PSV", "", 0, 0, 100),
        new(FluidHardwareKind.Filter, "treatment", HardwareRole.Passive, "FL", "kPa", 12.5, 0, 500),
        new(FluidHardwareKind.PulseDamper, "treatment", HardwareRole.Passive, "PD", "MPa", 0.25, 0, 2),
        new(FluidHardwareKind.FlowMeter, "measurement", HardwareRole.Monitor, "FT", "L/min", 12.5, 0, 200),
        new(FluidHardwareKind.PressureGauge, "measurement", HardwareRole.Monitor, "PT", "MPa", 0.28, -0.1, 2),
        new(FluidHardwareKind.TemperatureSensor, "measurement", HardwareRole.Monitor, "TT", "°C", 45.2, -50, 300),
        new(FluidHardwareKind.LevelSensor, "measurement", HardwareRole.Monitor, "LT", "%", 62.5, 0, 100),
        new(FluidHardwareKind.ConductivitySensor, "measurement", HardwareRole.Monitor, "CT", "µS/cm", 0.8, 0, 100000),
        new(FluidHardwareKind.PhSensor, "measurement", HardwareRole.Monitor, "pH", "pH", 7.2, 0, 14),
        new(FluidHardwareKind.LeakSensor, "measurement", HardwareRole.Monitor, "DI", "", 0, 0, 100)
    ];
}

/// <summary>控件库的独立演示数据，不是装机元件，不含 DO、PLC 路径或服务调用。</summary>
public sealed class HardwareDemo(HardwareDefinition definition) : ObservableObject
{
    public HardwareDefinition Definition { get; } = definition;
    private double _powerPercent = 65;
    public double PowerPercent { get => _powerPercent; set { if (double.IsFinite(value) && value is >= 0 and <= 100 && SetProperty(ref _powerPercent, value)) OnPropertyChanged(nameof(PowerFraction)); } }
    public double PowerFraction => PowerPercent / 100;
    private HardwareState _state = definition.Role == HardwareRole.Monitor ? HardwareState.Active : HardwareState.Stopped;
    public HardwareState State { get => _state; set { if (SetProperty(ref _state, value)) { OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(DiActive)); } } }
    public bool IsDi => Definition.Kind == FluidHardwareKind.LeakSensor;
    public bool? DiActive => State == HardwareState.Unknown ? null : State == HardwareState.Fault;
    public string ActiveText => L10n.Get(IsDi ? "hw.di.on" : "hw.state." + (Definition.Role switch { HardwareRole.Actuator => "run", HardwareRole.Valve => "open", _ => "normal" }));
    public string StopText => L10n.Get(IsDi ? "hw.di.off" : "hw.state." + (Definition.Role == HardwareRole.Valve ? "closed" : "stopped"));
    private double? _reading = definition.HasReading ? definition.DefaultValue : null;
    public double? Reading
    {
        get => _reading;
        set
        {
            if (value.HasValue && (!double.IsFinite(value.Value) || value < Definition.Minimum || value > Definition.Maximum)) return;
            if (SetProperty(ref _reading, value) && value.HasValue && State == HardwareState.Unknown) State = HardwareState.Stopped;
        }
    }
    private double? _target = definition.HasTarget ? definition.DefaultValue : null;
    public double? Target { get => _target; set { if (!value.HasValue || double.IsFinite(value.Value) && value >= Definition.Minimum && value <= Definition.Maximum) SetProperty(ref _target, value); } }
    private string _unit = definition.DefaultUnit;
    public string Unit { get => _unit; set => SetProperty(ref _unit, value); }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    private double _x = 20;
    public double X { get => _x; set { if (double.IsFinite(value)) SetProperty(ref _x, Math.Clamp(value, 0, 280 - SymbolWidth)); } }
    private double _y = 30;
    public double Y { get => _y; set { if (double.IsFinite(value)) SetProperty(ref _y, Math.Clamp(value, 0, 220 - SymbolHeight)); } }
    private double _symbolWidth = 240;
    public double SymbolWidth { get => _symbolWidth; set { if (double.IsFinite(value) && value is >= 80 and <= 260 && SetProperty(ref _symbolWidth, value)) { X = X; OnPropertyChanged(nameof(MaxX)); } } }
    private double _symbolHeight = 150;
    public double SymbolHeight { get => _symbolHeight; set { if (double.IsFinite(value) && value is >= 80 and <= 180 && SetProperty(ref _symbolHeight, value)) { Y = Y; OnPropertyChanged(nameof(MaxY)); } } }
    public double MaxX => 280 - SymbolWidth;
    public double MaxY => 220 - SymbolHeight;
    private double _angle;
    public double Angle { get => _angle; set { if (double.IsFinite(value) && value is >= -360 and <= 360) SetProperty(ref _angle, value); } }
    public string StateText => IsDi ? L10n.Get(DiActive switch { true => "hw.di.on", false => "hw.di.off", _ => "hw.state.unknown" }) : State switch { HardwareState.Active => ActiveText, HardwareState.Stopped => StopText,
        HardwareState.Fault => L10n.Get("hw.state.fault"), _ => L10n.Get("hw.state.unknown") };
}
