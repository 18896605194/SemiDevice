namespace xyz.Shared.Dtos;

/// <summary>
/// 库里的一个 CJ（control_job 表一行）；它下面的 PJ 按行号另取。
/// </summary>
public class ControlJobHistoryDto
{
    /// <summary>
    /// 行号：取这个 CJ 下面的 PJ 用（CJ 名以后可能再用，按名字找不准）
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// CJ 名
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public string LoadPort { get; set; } = string.Empty;

    public string CarrierId { get; set; } = string.Empty;

    public string LotId { get; set; } = string.Empty;

    /// <summary>
    /// E94 状态值：0 QUEUED、1 SELECTED、2 WAITINGFORSTART、3 EXECUTING、4 PAUSED、5 COMPLETED
    /// </summary>
    public int State { get; set; }

    /// <summary>
    /// 进 COMPLETED 走的转换号：10 正常、11 停止、12 中止；没完成为 0
    /// </summary>
    public int CompletedBy { get; set; }

    /// <summary>
    /// 删掉走的转换号：2 排队时删、13 完成后删；没删为 0
    /// </summary>
    public int EndedBy { get; set; }

    /// <summary>
    /// 设备重启时还没做完，开机记成了中止
    /// </summary>
    public bool Restarted { get; set; }

    /// <summary>
    /// 建 CJ 的时刻
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
