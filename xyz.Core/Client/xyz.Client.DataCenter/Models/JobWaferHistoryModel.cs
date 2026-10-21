using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.DataCenter.Models;

/// <summary>
/// Job 历史页晶圆表的一行：PJ 里的一片，带它的一行任务
/// </summary>
public class JobWaferHistoryModel
{
    public JobWaferHistoryModel(JobWaferDto dto, bool isProcessJobEnded)
    {
        Source = L10n.Get("jobhistory.location", dto.SourcePort, dto.SourceSlot);
        WaferId = dto.WaferId;
        Return = L10n.Get("jobhistory.location", dto.ReturnPort, dto.ReturnSlot);
        Result = ResultOf(dto, isProcessJobEnded);
        Tasks = dto.Tasks.Select(task => new JobTaskHistoryModel(task)).ToList();
    }

    /// <summary>
    /// 从哪个 LoadPort 第几槽取
    /// </summary>
    public string Source { get; }

    public string WaferId { get; }

    /// <summary>
    /// 回到哪个 LoadPort 第几槽
    /// </summary>
    public string Return { get; }

    public JobResult Result { get; }

    public IReadOnlyList<JobTaskHistoryModel> Tasks { get; }

    /// <summary>
    /// 有一格出错算出错，每一格都做完算完成；PJ 都结束了还没走完的算没做完，PJ 没结束的算还在做
    /// </summary>
    private static JobResult ResultOf(JobWaferDto dto, bool isProcessJobEnded)
    {
        if (dto.Tasks.Any(task => task.State == "Error"))
        {
            return JobResult.Error;
        }

        if (dto.Tasks.All(task => task.State == "Done"))
        {
            return JobResult.Completed;
        }

        if (isProcessJobEnded)
        {
            return JobResult.Unfinished;
        }

        return JobResult.Running;
    }
}
