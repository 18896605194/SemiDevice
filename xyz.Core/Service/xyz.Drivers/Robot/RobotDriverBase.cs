namespace xyz.Drivers.Robot;

/// <summary>
/// Robot 驱动基类。具体品牌实现设备连接和指令，不承担搬运调度及模块状态流转。
/// 动作返回 true 表示设备已确认动作完成，而非仅发送成功。
/// </summary>
public abstract class RobotDriverBase
{
    /// <summary>
    /// 设备连接是否可用。
    /// </summary>
    public abstract bool IsConnected { get; }

    /// <summary>
    /// 打开设备连接。
    /// </summary>
    public abstract bool Open();

    /// <summary>
    /// 关闭设备连接并释放通信资源。
    /// </summary>
    public abstract void Close();

    /// <summary>
    /// 执行回零。
    /// </summary>
    public abstract bool Home();

    /// <summary>
    /// 执行设备复位。
    /// </summary>
    public abstract bool Reset();

    /// <summary>
    /// 中止当前动作。实现时应允许在其他动作等待设备完成期间发出中止指令。
    /// </summary>
    public abstract bool Abort();

    /// <summary>
    /// 使用指定手臂从工位的目标槽位取片。
    /// </summary>
    /// <param name="arm">手臂号。</param>
    /// <param name="station">工位号。</param>
    /// <param name="slot">槽位号。</param>
    public abstract bool Pick(int arm, int station, int slot);

    /// <summary>
    /// 使用指定手臂向工位的目标槽位放片。
    /// </summary>
    /// <param name="arm">手臂号。</param>
    /// <param name="station">工位号。</param>
    /// <param name="slot">槽位号。</param>
    public abstract bool Place(int arm, int station, int slot);
}
