namespace xyz.Components.Interfaces;

public interface IAxis : IDeviceComponent
{
    /// <summary>
    /// 当前反馈是否有效
    /// </summary>
    bool HasPlcData { get; }

    /// <summary>
    /// 实际位置
    /// </summary>
    double CurrentPosition { get; }

    /// <summary>
    /// 实际速度
    /// </summary>
    double CurrentSpeed { get; }

    /// <summary>
    /// 是否已使能
    /// </summary>
    bool IsServoOn { get; }

    /// <summary>
    /// 是否已回零
    /// </summary>
    bool IsHomed { get; }

    /// <summary>
    /// 是否正在运动
    /// </summary>
    bool IsBusy { get; }

    /// <summary>
    /// 是否到达目标位置
    /// </summary>
    bool IsInPosition { get; }

    /// <summary>
    /// 是否报错
    /// </summary>
    bool IsError { get; }

    /// <summary>
    /// 回原点
    /// </summary>
    /// <returns></returns>
    bool Home();

    /// <summary>
    /// 绝对定位
    /// </summary>
    /// <param name="position"></param>
    /// <param name="speed"></param>
    /// <returns></returns>
    bool MoveTo(double position, double? speed = null);

    /// <summary>
    /// 相对移动
    /// </summary>
    /// <param name="offset"></param>
    /// <param name="speed"></param>
    /// <returns></returns>
    bool MoveBy(double offset, double? speed = null);

    /// <summary>
    /// Jog 运动
    /// </summary>
    /// <param name="speed"></param>
    /// <returns></returns>
    bool Jog(double speed);

    /// <summary>
    /// 连续旋转 SpinMotor
    /// </summary>
    /// <param name="speed"></param>
    /// <returns></returns>
    bool Spin(double speed);

    /// <summary>
    /// 停止
    /// </summary>
    /// <returns></returns>
    bool Stop();

    /// <summary>
    /// 急停
    /// </summary>
    /// <returns></returns>
    bool EmergencyStop();

    /// <summary>
    /// 复位驱动器
    /// </summary>
    /// <returns></returns>
    bool ResetDrive();

    /// <summary>
    /// 使能
    /// </summary>
    /// <param name="enabled"></param>
    /// <returns></returns>
    bool SetServo(bool enabled);
}

