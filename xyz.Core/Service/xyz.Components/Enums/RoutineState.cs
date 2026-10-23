namespace xyz.Components.Enums;

/// <summary>
/// 动作（ModuleRoutine）走到哪了
/// </summary>
public enum RoutineState
{
    /// <summary>
    /// 在走
    /// </summary>
    Running,

    /// <summary>
    /// 做成了
    /// </summary>
    Completed,

    /// <summary>
    /// 失败了
    /// </summary>
    Failed,

    /// <summary>
    /// 被中止了
    /// </summary>
    Aborted,
}
