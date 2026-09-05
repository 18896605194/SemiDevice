namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// 单指令动作操作的步骤。
/// </summary>
public enum ActionStep
{
    /// <summary>
    /// 发送指令（瞬时步：提交即完成本步）。
    /// </summary>
    SendCommand,

    /// <summary>
    /// 等待指令终态（持续步：每周期看一眼，超时判负）。
    /// </summary>
    WaitCommand,
}
