namespace xyz.Modules;

/// <summary>
/// 操作状态：Running 执行中；Completed / Failed / Aborted 为终态。
/// </summary>
public enum OperationState
{
    /// <summary>
    /// 执行中。
    /// </summary>
    Running,

    /// <summary>
    /// 成功终结。
    /// </summary>
    Completed,

    /// <summary>
    /// 失败终结（指令失败、超时、步骤异常）。
    /// </summary>
    Failed,

    /// <summary>
    /// 被宿主打断（Abort 顶替）。
    /// </summary>
    Aborted,
}
