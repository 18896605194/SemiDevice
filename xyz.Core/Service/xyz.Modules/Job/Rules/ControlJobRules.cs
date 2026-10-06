using xyz.Components.Enums;

namespace xyz.Modules;

/// <summary>
/// CJ 的自动转换规则：一条规则一个小类，只回答"这个 CJ 现在该不该自动往下转、转哪条"（不该转返回 null）。
/// </summary>
internal interface IControlJobRule
{
    CtrlJobTrigger? Evaluate(ControlJob job, JobRuntime runtime);
}

/// <summary>
/// #3 QUEUED → SELECTED：排在队首（排队的 CJ 里第一个），在跑的 CJ（选中到暂停）没到上限（JobManager 的 SC MaxActiveControlJobs）。
/// </summary>
internal sealed class SelectRule : IControlJobRule
{
    public CtrlJobTrigger? Evaluate(ControlJob job, JobRuntime runtime)
    {
        if (job.State != CtrlJobState.Queued)
        {
            return null;
        }

        var head = runtime.Book.ControlJobs.FirstOrDefault(control => control.State == CtrlJobState.Queued);
        if (!ReferenceEquals(head, job))
        {
            return null;
        }

        int active = runtime.Book.ControlJobs.Count(control => E94Transitions.Active.Contains(control.State));
        return active < Math.Max(1, runtime.Limits.MaxActiveControlJobs()) ? CtrlJobTrigger.Select : null;
    }
}

/// <summary>
/// #5 / #6 SELECTED → EXECUTING / WAITINGFORSTART：料到了（LoadPort 上的载具 Load 好、能取片），自动启动的直接执行，手动启动的等 CJStart。
/// </summary>
internal sealed class MaterialReadyRule : IControlJobRule
{
    public CtrlJobTrigger? Evaluate(ControlJob job, JobRuntime runtime)
    {
        if (job.State != CtrlJobState.Selected || !runtime.Environment.IsCarrierReady(job.LoadPort))
        {
            return null;
        }

        return job.AutoStart ? CtrlJobTrigger.MaterialReadyStart : CtrlJobTrigger.MaterialReadyWait;
    }
}

/// <summary>
/// #10 EXECUTING → COMPLETED：没收 Stop / Abort，下面的 PJ 都结束了。
/// </summary>
internal sealed class AllDoneRule : IControlJobRule
{
    public CtrlJobTrigger? Evaluate(ControlJob job, JobRuntime runtime)
    {
        if (job.State != CtrlJobState.Executing || job.Ending != CtrlJobEnding.None
            || job.ProcessJobs.Count == 0 || !job.ProcessJobs.All(process => process.IsEnded))
        {
            return null;
        }

        return CtrlJobTrigger.AllDone;
    }
}

/// <summary>
/// #11 / #12 ACTIVE → COMPLETED：收了 Stop / Abort，下面的 PJ 都结束了（停完、中止完，片位都确定）。
/// </summary>
internal sealed class EndingDoneRule : IControlJobRule
{
    public CtrlJobTrigger? Evaluate(ControlJob job, JobRuntime runtime)
    {
        if (job.Ending == CtrlJobEnding.None || !job.ProcessJobs.All(process => process.IsEnded))
        {
            return null;
        }

        return job.Ending == CtrlJobEnding.Stop ? CtrlJobTrigger.Stopped : CtrlJobTrigger.Aborted;
    }
}

/// <summary>
/// #13 COMPLETED → 删掉：载具从 LoadPort 拿走了（或换了一个），完成的 CJ 转进历史。载具还在时留着给人看结果。
/// </summary>
internal sealed class DeleteRule : IControlJobRule
{
    public CtrlJobTrigger? Evaluate(ControlJob job, JobRuntime runtime)
    {
        if (job.State != CtrlJobState.Completed)
        {
            return null;
        }

        var port = runtime.Environment.LoadPort(job.LoadPort);
        var carrier = port?.Carrier;
        bool sameCarrier = port is not null && port.IsPodPlaced && carrier is not null
            && (job.CarrierInstance is null || carrier.Id == job.CarrierInstance.Value);
        return sameCarrier ? null : CtrlJobTrigger.Delete;
    }
}
