namespace xyz.Client.DataCenter.Models;

/// <summary>
/// Job 历史页"结果"一列（CJ、PJ、每片共用）：界面按它换字、换色
/// </summary>
public enum JobResult
{
    /// <summary>
    /// 还没结束（排队、执行中、暂停）
    /// </summary>
    Running,

    /// <summary>
    /// 正常做完
    /// </summary>
    Completed,

    /// <summary>
    /// 停止收场
    /// </summary>
    Stopped,

    /// <summary>
    /// 中止收场
    /// </summary>
    Aborted,

    /// <summary>
    /// 设备重启时还没做完，开机记成了中止（只有 CJ 有）
    /// </summary>
    Restarted,

    /// <summary>
    /// 没开始就删了
    /// </summary>
    Cancelled,

    /// <summary>
    /// 有任务出错（只有片有）
    /// </summary>
    Error,

    /// <summary>
    /// PJ 结束了这片还没走完（只有片有）
    /// </summary>
    Unfinished,
}
