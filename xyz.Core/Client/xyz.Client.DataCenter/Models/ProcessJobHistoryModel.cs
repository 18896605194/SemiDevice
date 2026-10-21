using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.DataCenter.Models;

/// <summary>
/// Job 历史页 PJ 表的一行，带它的每一片
/// </summary>
public class ProcessJobHistoryModel
{
    public ProcessJobHistoryModel(ProcessJobDto dto)
    {
        Name = dto.Id;
        Sequence = dto.Sequence;
        if (dto.SequenceRevision > 0)
        {
            Sequence = L10n.Get("jobhistory.revision", dto.Sequence, dto.SequenceRevision);
        }

        WaferCount = dto.Wafers.Count;
        Result = ResultOf(dto.EndedBy);
        StartedAt = dto.StartedAt;
        EndedAt = dto.EndedAt;
        Wafers = dto.Wafers.Select(wafer => new JobWaferHistoryModel(wafer, dto.EndedBy != 0)).ToList();
    }

    public string Name { get; }

    /// <summary>
    /// 流程配方名带版本
    /// </summary>
    public string Sequence { get; }

    public int WaferCount { get; }

    public JobResult Result { get; }

    public DateTime? StartedAt { get; }

    public DateTime? EndedAt { get; }

    public IReadOnlyList<JobWaferHistoryModel> Wafers { get; }

    /// <summary>
    /// 结果按 E40 结束的转换号认：#7 正常、#16 中止、#17 停止、#18 排队时删；没结束是 0
    /// </summary>
    private static JobResult ResultOf(int endedBy)
    {
        switch (endedBy)
        {
            case 7:
                return JobResult.Completed;
            case 16:
                return JobResult.Aborted;
            case 17:
                return JobResult.Stopped;
            case 18:
                return JobResult.Cancelled;
            default:
                return JobResult.Running;
        }
    }
}
