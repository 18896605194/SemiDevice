namespace xyz.Modules;

/// <summary>
/// 调度对一个 PJ 能做什么。
/// </summary>
[Flags]
public enum JobDispatch
{
    /// <summary>什么都不做（没开始、等启动、暂停到位、中止中、结束了）。</summary>
    None = 0,

    /// <summary>推进机内的片（去下一站、起工艺、回片）。</summary>
    Advance = 1,

    /// <summary>从载具投新片。</summary>
    Feed = 2,
}

/// <summary>
/// 闸门：PJ / CJ 的状态 → 调度许可。暂停、停止的效果就体现在这张表上——调度器不认识"暂停"，只看许可：
/// PAUSING、STOPPING 只推进机内的片、不投新片，机内没片了规则再把它转成 PAUSED / 结束。
/// </summary>
public static class JobGates
{
    /// <summary>这个 PJ 此刻的调度许可。片位说不准（要人工恢复确认）的 PJ 一律不动。</summary>
    public static JobDispatch Of(ProcessJob job)
    {
        if (job.IsEnded || job.NeedsRecovery)
        {
            return JobDispatch.None;
        }

        return job.State switch
        {
            PrJobState.Processing => JobDispatch.Advance | JobDispatch.Feed,
            PrJobState.Pausing or PrJobState.Stopping or PrJobState.ProcessComplete => JobDispatch.Advance,
            _ => JobDispatch.None,
        };
    }

    /// <summary>
    /// CJ 能不能启动下面新的 PJ：在执行、没收 Stop / Abort。CJ 暂停（E94）就是这里关上——在跑的 PJ 不受影响。
    /// </summary>
    public static bool CanStartProcessJobs(ControlJob job)
    {
        return job.State == CtrlJobState.Executing && job.Ending == CtrlJobEnding.None;
    }
}
