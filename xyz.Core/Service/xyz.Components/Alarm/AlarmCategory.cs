namespace xyz.Components.Alarm;

/// <summary>
/// 报警分类。
/// </summary>
public enum AlarmCategory
{
    SafetyInterlock,
    MotionError,
    Timeout,
    ParameterControlError,
    SensorError,
    HardwareError,
    CommunicationError,
    ProcessError,
    Other
}
