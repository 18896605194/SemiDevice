namespace xyz.Components;

/// <summary>
/// 组件一个动作的状态：指令写进 PLC 即 Running，完成/失败由组件自己的扫描判定；Abort 打断回到 Idle。
/// 轴、气缸、阀都用它，调用方发完指令后看它等结果。
/// </summary>
public enum ActionState
{
    Idle,
    Running,
    Completed,
    Failed,
}
