namespace xyz.Modules;

/// <summary>
/// 设备状态轮询表里的一格查什么（见 BaseRobotModule.QueryOrder）。
/// </summary>
public enum RobotQueryKind
{
    SubscribeWaferEvent,

    ServoOn,

    DeviceError,

    Speed,

    AxisPos,
}
