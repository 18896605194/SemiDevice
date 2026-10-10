namespace xyz.Modules;

/// <summary>
/// 设备状态轮询这一条查询问的是什么；回包按它落模块状态，不认品牌指令类型。
/// </summary>
public enum RobotQueryKind
{
    SubscribeWaferEvent,

    ServoOn,

    DeviceError,

    Speed,

    AxisPos,
}
