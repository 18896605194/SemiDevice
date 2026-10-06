using xyz.Components.Enums;

namespace xyz.Modules;

/// <summary>
/// PJ 的自动转换规则：一条规则一个小类，只回答"这个 PJ 现在该不该自动往下转、转哪条"（不该转返回 null），转由引擎做。
/// JobManager 每拍对每个没结束的 PJ 挨条问，转了再问一遍，直到不再转。
/// </summary>
internal interface IProcessJobRule
{
    PrJobTrigger? Evaluate(ProcessJob job, JobRuntime runtime);
}

/// <summary>
/// #2 QUEUED/POOLED → SETTING UP：所属 CJ 在执行（没暂停、没收 Stop / Abort），并且轮到它了——
/// CJ 里排在它前面的 PJ 都投完了片（或结束了）。一个 PJ 一个 PJ 地投，片在机内的加工照样能叠着做。
/// </summary>
internal sealed class SetupRule : IProcessJobRule
{
    public PrJobTrigger? Evaluate(ProcessJob job, JobRuntime runtime)
    {
        var control = job.ControlJob;
        if (job.State != PrJobState.QueuedPooled || control is null || !JobGates.CanStartProcessJobs(control))
        {
            return null;
        }

        foreach (var earlier in control.ProcessJobs)
        {
            if (ReferenceEquals(earlier, job))
            {
                break;
            }

            if (!earlier.IsEnded && earlier.HasWaitingWafers)
            {
                return null;
            }
        }

        return PrJobTrigger.Setup;
    }
}

/// <summary>
/// #3 / #4 SETTING UP → WAITING FOR START / PROCESSING：片都还在（没被认成片位不对），自动启动的直接开始，手动启动的等 Start。
/// </summary>
internal sealed class SetupDoneRule : IProcessJobRule
{
    public PrJobTrigger? Evaluate(ProcessJob job, JobRuntime runtime)
    {
        if (job.State != PrJobState.SettingUp || job.NeedsRecovery || job.Wafers.Any(wafer => wafer.Phase == JobWaferPhase.Lost))
        {
            return null;
        }

        return job.AutoStart ? PrJobTrigger.SetupDoneStart : PrJobTrigger.SetupDoneWait;
    }
}

/// <summary>
/// #6 PROCESSING → PROCESS COMPLETE：片都投了，每片的加工都做完了（还在回片路上也算）。
/// </summary>
internal sealed class ProcessDoneRule : IProcessJobRule
{
    public PrJobTrigger? Evaluate(ProcessJob job, JobRuntime runtime)
    {
        int steps = job.Recipe.Steps.Count;
        if (job.State != PrJobState.Processing || !job.Wafers.All(wafer => wafer.IsProcessFinished(steps)))
        {
            return null;
        }

        return PrJobTrigger.ProcessDone;
    }
}

/// <summary>
/// #7 PROCESS COMPLETE → 结束：片都回到回片槽，没有在途的动作。
/// </summary>
internal sealed class MaterialOutRule : IProcessJobRule
{
    public PrJobTrigger? Evaluate(ProcessJob job, JobRuntime runtime)
    {
        if (job.State != PrJobState.ProcessComplete || job.HasInFlight
            || !job.Wafers.All(wafer => wafer.Phase == JobWaferPhase.Done))
        {
            return null;
        }

        return PrJobTrigger.MaterialOut;
    }
}

/// <summary>
/// #9 PAUSING → PAUSED：机内已经没有这个 PJ 的片了（投出去的都回来了），也没有在途的动作。
/// </summary>
internal sealed class PauseDoneRule : IProcessJobRule
{
    public PrJobTrigger? Evaluate(ProcessJob job, JobRuntime runtime)
    {
        if (job.State != PrJobState.Pausing || job.HasWafersInMachine || job.HasInFlight)
        {
            return null;
        }

        return PrJobTrigger.PauseDone;
    }
}

/// <summary>
/// #17 STOPPING → 结束：机内的片都走完回片了，没有在途的动作。没投的片结束时记成未执行。
/// </summary>
internal sealed class StopDoneRule : IProcessJobRule
{
    public PrJobTrigger? Evaluate(ProcessJob job, JobRuntime runtime)
    {
        if (job.State != PrJobState.Stopping || job.HasWafersInMachine || job.HasInFlight)
        {
            return null;
        }

        return PrJobTrigger.StopDone;
    }
}

/// <summary>
/// #16 ABORTING → 结束：在途的动作都结束了，发给腔体的中止都做完了（设备确认了），片位都确定（没有说不准的片、没有留着锁等确认的搬运单）。
/// 片位说不准时不往下转，标成要人工恢复确认——仅仅删掉 Job 对象不能算中止完成。
/// </summary>
internal sealed class AbortDoneRule : IProcessJobRule
{
    public PrJobTrigger? Evaluate(ProcessJob job, JobRuntime runtime)
    {
        if (job.State != PrJobState.Aborting || job.HasInFlight || job.DeviceAborts.Any(abort => !abort.IsSettled))
        {
            return null;
        }

        bool lost = job.Wafers.Any(wafer => wafer.Phase == JobWaferPhase.Lost);
        bool held = runtime.Environment.Transfers?.HeldResults.Any(result => string.Equals(result.Owner, job.Id, StringComparison.Ordinal)) == true;
        if (lost || held)
        {
            if (!job.NeedsRecovery)
            {
                job.NeedsRecovery = true;
                runtime.Book.Touch();
            }

            return null;
        }

        return PrJobTrigger.AbortDone;
    }
}
