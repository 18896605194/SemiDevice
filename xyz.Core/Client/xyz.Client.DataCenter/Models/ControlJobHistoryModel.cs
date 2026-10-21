using xyz.Shared.Dtos;

namespace xyz.Client.DataCenter.Models;

/// <summary>
/// Job 历史页 CJ 表的一行
/// </summary>
public class ControlJobHistoryModel
{
    public ControlJobHistoryModel(ControlJobHistoryDto dto)
    {
        Id = dto.Id;
        Name = dto.Name;
        LoadPort = dto.LoadPort;
        CarrierId = dto.CarrierId;
        LotId = dto.LotId;
        Result = ResultOf(dto);
        CreatedAt = dto.CreatedAt;
        StartedAt = dto.StartedAt;
        CompletedAt = dto.CompletedAt;
    }

    /// <summary>
    /// 库里的行号，取它下面的 PJ 用
    /// </summary>
    public long Id { get; }

    public string Name { get; }

    public string LoadPort { get; }

    public string CarrierId { get; }

    public string LotId { get; }

    public JobResult Result { get; }

    public DateTime CreatedAt { get; }

    public DateTime? StartedAt { get; }

    public DateTime? CompletedAt { get; }

    /// <summary>
    /// 结果按 E94 转换号认：排队时删的是取消，进 COMPLETED 走的 #10 / #11 / #12 是完成 / 停止 / 中止，都没有就是还没结束
    /// </summary>
    private static JobResult ResultOf(ControlJobHistoryDto dto)
    {
        if (dto.EndedBy == 2)
        {
            return JobResult.Cancelled;
        }

        switch (dto.CompletedBy)
        {
            case 10:
                return JobResult.Completed;
            case 11:
                return JobResult.Stopped;
            case 12:
                if (dto.Restarted)
                {
                    return JobResult.Restarted;
                }

                return JobResult.Aborted;
            default:
                return JobResult.Running;
        }
    }
}
