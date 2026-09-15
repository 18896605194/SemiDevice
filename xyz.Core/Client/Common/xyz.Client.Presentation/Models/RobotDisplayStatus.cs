namespace xyz.Client.Presentation.Models;

/// <summary>
/// 机械手控件的状态显示，决定底座状态环的颜色与动画。
/// </summary>
public enum RobotDisplayStatus
{
    /// <summary>
    /// 未连接：灰色。
    /// </summary>
    Offline,

    /// <summary>
    /// 未初始化（需要回原点）：黄色。
    /// </summary>
    NotReady,

    /// <summary>
    /// 空闲：绿色。
    /// </summary>
    Idle,

    /// <summary>
    /// 动作中：蓝色流光环绕。
    /// </summary>
    Busy,

    /// <summary>
    /// 报警：红色呼吸闪烁。
    /// </summary>
    Alarm,
}
