namespace xyz.Components.Interfaces;

/// <summary>
/// 机械手指：本身是一根轴，另有手指号、手上有没有片、在位传感器。
/// </summary>
public interface IRobotArm : IRobotAxis
{
    /// <summary>手指号（取放、主动推送协议用，从 1 开始；也是晶圆账上的槽号）。</summary>
    int Number { get; }

    /// <summary>在位传感器（节点下名叫 WaferSensor 的 DI）；没挂为 null，就当没有传感器，不跟晶圆账比。</summary>
    IDiSensor? WaferSensor { get; }

    /// <summary>手上有没有片：挂了在位传感器按它，没挂按控制器推送；null = 还没读到。</summary>
    bool? HasWafer { get; }

    /// <summary>取放做完后等在位传感器跟上的上限（ms）。</summary>
    int WaferSensorTimeoutMs { get; }

    /// <summary>记下控制器推送的手指在位（驱动路由线程上调，只翻标志）。</summary>
    void UpdateWaferPresence(bool hasWafer);
}
