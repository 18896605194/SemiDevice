namespace xyz.Components.Interfaces;

/// <summary>
/// Robot Arm
/// </summary>
public interface IRobotArm : IRobotAxis
{
   
    int Number { get; }
    IDiSensor? WaferSensor { get; }

    bool? HasWafer { get; }

    /// <summary>
    /// 账上这只手有没有片
    /// </summary>
    bool LedgerHasWafer { get; }

    /// <summary>
    /// 记下这只手属于哪台机械手（晶圆账按机械手名 + 手指号记）
    /// </summary>
    void SetRobot(string robot);

    /// <summary>取放做完后等在位传感器跟上的上限（ms）。</summary>
    int WaferSensorTimeoutMs { get; }

    /// <summary>记下控制器推送的手指在位（驱动路由线程上调，只翻标志）。</summary>
    void UpdateWaferPresence(bool hasWafer);
}
