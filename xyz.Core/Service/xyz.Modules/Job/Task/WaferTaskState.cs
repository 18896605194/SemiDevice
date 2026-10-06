namespace xyz.Modules;

/// <summary>
/// 任务的状态（任务表里一格）。
/// </summary>
public enum WaferTaskState
{
    /// <summary>等着做：还没轮到，或轮到了还没派出去。</summary>
    Waiting,

    /// <summary>进行中：搬运单或站内任务在跑。</summary>
    Running,

    /// <summary>完成（设备做完的，或人标记完成的）。</summary>
    Done,

    /// <summary>出错：做到一半出错，停在这里等人处理（重做，或人做完了标记完成）；这一行后面的任务都等着。</summary>
    Error,

    /// <summary>未执行：Job 结束时还没做的。</summary>
    Cancelled,
}
