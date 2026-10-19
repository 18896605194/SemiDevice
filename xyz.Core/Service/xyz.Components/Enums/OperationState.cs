namespace xyz.Components.Enums;

/// <summary>
/// Operation状态
/// </summary>
public enum OperationState
{
    /// <summary>
    /// 执行中。
    /// </summary>
    Running,

    /// <summary>
    /// 完成
    /// </summary>
    Completed,

    /// <summary>
    /// error
    /// </summary>
    Failed,

    /// <summary>
    /// 被终止
    /// </summary>
    Aborted,
}
