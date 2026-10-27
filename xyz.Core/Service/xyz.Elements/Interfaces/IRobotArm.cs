namespace xyz.Elements.Interfaces;

/// <summary>
/// Robot Arm
/// </summary>
public interface IRobotArm : IRobotAxis
{
   /// <summary>
   /// Arm 号
   /// </summary>
    int Number { get; }

    IDiSensor? WaferSensor { get; }

    /// <summary>
    /// 启用持片检查
    /// </summary>
    bool IsWaferCheckEnabled { get; }

    /// <summary>
    /// 实际持片反馈：true 有片，false 没片
    /// </summary>
    bool? HasWafer { get; }

    /// <summary>
    /// 账上这只手有没有片
    /// </summary>
    bool LedgerHasWafer { get; }

    /// <summary>
    /// 记下这只手属于哪台机械手（晶圆账按机械手名 + 手指号记）
    /// </summary>
    void SetRobot(string robot);

    /// <summary>
    /// 取放做完后等在位传感器跟上的上限（ms）。
    /// </summary>
    int WaferSensorTimeoutMs { get; }

    /// <summary>
    /// 底层推送的手指
    /// </summary>
    /// <param name="hasWafer"></param>
    void UpdateWaferPresence(bool hasWafer);
}
