using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// Job 运行对象 → DTO（推送、查询、EAP 上报都用这一份）。在 JobManager 的扫描线程上转，转出来的是副本，拿到就不变。
/// </summary>
internal static class JobDtos
{
    public static ControlJobDto Of(ControlJob job)
    {
        return new ControlJobDto
        {
            Id = job.Id,
            LoadPort = job.LoadPort,
            CarrierId = job.CarrierId ?? string.Empty,
            LotId = job.LotId ?? string.Empty,
            State = (int)job.State,
            AutoStart = job.AutoStart,
            Ending = job.Ending.ToString(),
            ProcessJobs = job.ProcessJobs.Select(process => process.Id).ToList(),
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            CompletedBy = job.CompletedBy ?? 0,
            EndedBy = job.EndedBy ?? 0,
            EndedAt = job.EndedAt,
            NeedsRecovery = job.NeedsRecovery,
        };
    }

    public static ProcessJobDto Of(ProcessJob job)
    {
        int steps = job.Recipe.Steps.Count;
        return new ProcessJobDto
        {
            Id = job.Id,
            ControlJob = job.ControlJob?.Id ?? string.Empty,
            Sequence = job.Recipe.SequenceName,
            SequenceRevision = job.Recipe.SequenceRevision,
            StepCount = steps,
            State = (int)job.State,
            AutoStart = job.AutoStart,
            Wafers = job.Wafers.Select(Of).ToList(),
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            EndedAt = job.EndedAt,
            EndedBy = job.EndedBy ?? 0,
            NeedsRecovery = job.NeedsRecovery,
        };
    }

    public static JobWaferDto Of(JobWafer wafer)
    {
        var wait = wafer.Wait;
        return new JobWaferDto
        {
            WaferId = wafer.Name,
            SourcePort = wafer.SourcePort,
            SourceSlot = wafer.SourceSlot,
            ReturnPort = wafer.ReturnPort,
            ReturnSlot = wafer.ReturnSlot,
            Step = wafer.Step,
            Phase = wafer.Phase.ToString(),
            Outcome = wafer.Outcome.ToString(),
            Station = (wafer.Phase == JobWaferPhase.Moving ? wafer.MovingTo : wafer.Station) ?? string.Empty,
            Slot = wafer.Phase == JobWaferPhase.Moving ? wafer.MovingToSlot : wafer.Slot,
            WaitCode = wait?.Code ?? string.Empty,
            WaitArgs = wait?.Args.ToList() ?? [],
            Results = wafer.Results.Select(Of).ToList(),
        };
    }

    public static JobStepResultDto Of(JobStepResult result)
    {
        return new JobStepResultDto
        {
            Step = result.Step,
            Station = result.Station,
            Recipe = result.Recipe,
            RecipeRevision = result.RecipeRevision,
            Success = result.Success,
            Code = result.Code,
            Args = result.Args.ToList(),
            Simulated = result.Simulated,
            StartedAt = result.StartedAt,
            EndedAt = result.EndedAt,
        };
    }

    /// <summary>
    /// 整个账本的一份快照：没删的 CJ、界面要看的 PJ（没结束的，加上没删的 CJ 下面已经结束的）、历史（本次的在前，上次开机留下的在后）、派单暂停的原因。
    /// </summary>
    public static JobListDto Of(JobBook book, long version, JobWait? hold)
    {
        var processJobs = new List<ProcessJob>();
        foreach (var control in book.ControlJobs)
        {
            processJobs.AddRange(control.ProcessJobs);
        }

        foreach (var process in book.ProcessJobs)
        {
            if (!processJobs.Contains(process))
            {
                processJobs.Add(process);
            }
        }

        return new JobListDto
        {
            Version = version,
            ControlJobs = book.ControlJobs.Select(Of).ToList(),
            ProcessJobs = processJobs.Select(Of).ToList(),
            History = book.History.Select(Of).Concat(book.Restored).ToList(),
            IsHeld = hold is not null,
            HoldCode = hold?.Code ?? string.Empty,
            HoldArgs = hold?.Args.ToList() ?? [],
        };
    }
}
