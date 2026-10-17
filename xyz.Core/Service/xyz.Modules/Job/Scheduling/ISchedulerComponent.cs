namespace xyz.Modules;

/// <summary>
/// 调度：按任务表派搬运和站内任务、收结果
/// </summary>
public interface ISchedulerComponent
{
    /// <summary>
    /// 派这一拍的任务
    /// </summary>
    void Dispatch(IReadOnlyList<TaskRow> rows, ITaskComponent tasks);

    /// <summary>
    /// 收这一拍的结果
    /// </summary>
    void Collect(ITaskComponent tasks);
}
