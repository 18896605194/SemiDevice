namespace xyz.Elements.Enums;

/// <summary>
/// ModuleRoutine走到哪了
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
