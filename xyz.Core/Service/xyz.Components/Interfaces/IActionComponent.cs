using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

/// <summary>
/// 自己管动作到完成的组件（轴、气缸、阀）：调用方发完指令看 ActionState 等结果，手动页的部件动作也靠它判做完没有。
/// </summary>
public interface IActionComponent
{
    /// <summary>
    /// 当前动作的状态：指令写进 PLC 即 Running，做完 Completed，超时或设备报错 Failed，被中止回到 Idle。
    /// </summary>
    ActionState ActionState { get; }
}
