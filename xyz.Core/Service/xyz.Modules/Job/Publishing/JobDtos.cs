using xyz.Shared.Dtos;

using xyz.Components.Enums;

namespace xyz.Modules;

/// <summary>
/// Job 运行对象 → DTO（推送、查询、EAP 上报都用这一份）。在 Job 的扫描线程上转，转出来的是副本，拿到就不变。
/// </summary>
internal static class JobDtos
{
    public static ControlJobDto Of(ControlJob job, bool autoStart)
    {
        return new ControlJobDto
        {
            Id = job.Id,
            LoadPort = job.LoadPort,
            CarrierId = job.CarrierId ?? string.Empty,
            LotId = job.LotId ?? string.Empty,
            State = (int)job.State,
            E94State = E94StateOf(job),
            AutoStart = autoStart,
            Ending = job.Ending.ToString(),
            ProcessJobs = job.ProcessJobs.Select(process => process.Id).ToList(),
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            CompletedBy = job.CompletedBy ?? 0,
            EndedBy = job.EndedBy ?? 0,
            EndedAt = job.EndedAt,
        };
    }

    private static int E94StateOf(ControlJob job)
    {
        var state = job.State;
        if (state == ControlJobState.Aborting)
        {
            state = job.StateBeforeAbort;
        }

        switch (state)
        {
            case ControlJobState.Created:
            case ControlJobState.Queued:
                return 0;
            case ControlJobState.Selected:
                return 1;
            case ControlJobState.WaitingForStart:
                return 2;
            case ControlJobState.Executing:
                return 3;
            case ControlJobState.Paused:
                return 4;
            case ControlJobState.Aborted:
            case ControlJobState.Completed:
                return 5;
            default:
                throw new ArgumentOutOfRangeException(nameof(job), state, "未知 CJ 状态。");
        }
    }

    public static ProcessJobDto Of(ProcessJob job, bool autoStart)
    {
        return new ProcessJobDto
        {
            Id = job.Id,
            ControlJob = job.ControlJob?.Id ?? string.Empty,
            LotId = job.LotId ?? string.Empty,
            CarrierId = job.CarrierId ?? string.Empty,
            Sequence = job.Sequence.Name,
            SequenceRevision = job.Sequence.Revision,
            State = (int)job.State,
            AutoStart = autoStart,
            Wafers = job.Rows.Select(Of).ToList(),
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            EndedAt = job.EndedAt,
            EndedBy = job.EndedBy ?? 0,
        };
    }

    public static JobWaferDto Of(TaskRow row)
    {
        return new JobWaferDto
        {
            WaferId = row.WaferName,
            SourcePort = row.SourcePort,
            SourceSlot = row.SourceSlot,
            ReturnPort = row.ReturnPort,
            ReturnSlot = row.ReturnSlot,
            Tasks = row.Tasks.Select(Of).ToList(),
        };
    }

    public static JobTaskDto Of(WaferTask task)
    {
        return new JobTaskDto
        {
            Kind = task.Kind,
            Step = task.Step,
            Stations = task.Stations.ToList(),
            Station = task.Station ?? string.Empty,
            Slot = task.Slot,
            Recipe = task.RecipeName,
            RecipeRevision = task.Recipe?.Revision ?? 0,
            State = task.State.ToString(),
            Robot = task.Robot ?? string.Empty,
            Arm = task.Arm,
            Code = task.Code,
            Args = task.Args.ToList(),
        };
    }
}
