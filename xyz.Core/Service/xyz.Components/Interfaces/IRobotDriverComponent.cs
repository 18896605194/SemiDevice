using xyz.Drivers.Robot;

namespace xyz.Components.Interfaces;

/// <summary>
/// 机械手驱动组件（品牌指令由具体驱动出）
/// </summary>
public interface IRobotDriverComponent
{
    /// <summary>
    /// 连上了没有
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 设备主动报上来的事
    /// </summary>
    event Action<RobotDeviceEvent>? DeviceEvent;

    /// <summary>
    /// 回原点，被拒是 null
    /// </summary>
    RobotCommand? Home();

    /// <summary>
    /// 取片，被拒是 null
    /// </summary>
    RobotCommand? Pick(int arm, int station, int slot);

    /// <summary>
    /// 放片，被拒是 null
    /// </summary>
    RobotCommand? Place(int arm, int station, int slot);

    /// <summary>
    /// 上电，被拒是 null
    /// </summary>
    RobotCommand? PowerOn();

    /// <summary>
    /// 断电，被拒是 null
    /// </summary>
    RobotCommand? PowerOff();

    /// <summary>
    /// 停止，被拒是 null
    /// </summary>
    RobotCommand? Stop();

    /// <summary>
    /// 清设备报错，被拒是 null
    /// </summary>
    RobotCommand? ResetDrive();

    /// <summary>
    /// 查一根轴的坐标（轴名照 sc 的轴节点），被拒是 null
    /// </summary>
    RobotCommand? QueryAxisPos(string axis);

    /// <summary>
    /// 查设备报错，被拒是 null
    /// </summary>
    RobotCommand? QueryDeviceError();

    /// <summary>
    /// 查伺服上没上电，被拒是 null
    /// </summary>
    RobotCommand? QueryServoOn();

    /// <summary>
    /// 查速度，被拒是 null
    /// </summary>
    RobotCommand? QuerySpeed();

    /// <summary>
    /// 订阅手指在位的主动上报，被拒是 null
    /// </summary>
    RobotCommand? SubscribeWaferEvent();

    /// <summary>
    /// 作废一条在途指令
    /// </summary>
    void Abandon(RobotCommand command, string reason);

    /// <summary>
    /// 断开
    /// </summary>
    void Close();
}
