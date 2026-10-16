namespace xyz.Components.Interfaces;

/// <summary>
/// Robot Arm
/// </summary>
public interface IRobotArm : IRobotAxis
{
   
    int Number { get; }
    IDiSensor? WaferSensor { get; }

    bool? HasWafer { get; }

    /// <summary>取放做完后等在位传感器跟上的上限（ms）。</summary>
    int WaferSensorTimeoutMs { get; }

    /// <summary>记下控制器推送的手指在位（驱动路由线程上调，只翻标志）。</summary>
    void UpdateWaferPresence(bool hasWafer);
}
