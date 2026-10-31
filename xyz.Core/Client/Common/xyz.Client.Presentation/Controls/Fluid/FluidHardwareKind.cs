namespace xyz.Client.Presentation.Controls.Fluid;

public enum FluidHardwareKind
{
    Tank, CentrifugalPump, DiaphragmPump, MeteringPump, Mixer,
    InlineHeater, ImmersionHeater, HeatExchanger,
    OnOffValve, ManualValve, ProportionalValve, ThreeWayValve, CheckValve, PressureRegulator, ReliefValve,
    Filter, PulseDamper, FlowMeter, PressureGauge, TemperatureSensor, LevelSensor, ConductivitySensor, PhSensor, LeakSensor
}
public enum HardwareState { Unknown, Stopped, Active, Fault }
