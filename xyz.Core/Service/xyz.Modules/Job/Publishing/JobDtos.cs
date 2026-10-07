using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// Job 运行对象 → DTO（推送、查询、EAP 上报都用这一份）。在 Job 的扫描线程上转，转出来的是副本，拿到就不变。
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
        };
    }

    public static ProcessJobDto Of(ProcessJob job)
    {
        return new ProcessJobDto
        {
            Id = job.Id,
            ControlJob = job.ControlJob?.Id ?? string.Empty,
            CarrierId = job.CarrierId ?? string.Empty,
            Sequence = job.Sequence.Name,
            SequenceRevision = job.Sequence.Revision,
            State = (int)job.State,
            AutoStart = job.AutoStart,
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
