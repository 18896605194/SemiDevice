using SqlSugar;

namespace xyz.Database.Jobs;

/// <summary>
/// CJ 记录（SEMI E94 Control Job）：一个 CJ 一行，建好就插、之后每次变化更新这一行，删掉（#2 / #13）那次写最后一遍。
/// 主键是自增 Id（同一个 CJ 名以后还可能再用）；CreatedTime 是建 CJ 的时刻，UpdatedTime 是最后一次写的时刻。
/// 开机时还没删的（EndedBy = 0）就是重启前没做完的：记成中止（标着重启）。
/// </summary>
[SugarTable("control_job")]
public class ControlJobEntity : BaseEntity
{
    /// <summary>CJ 名（CtrlJobID）。</summary>
    [SugarColumn(Length = 80)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string LoadPort { get; set; } = string.Empty;

    [SugarColumn(Length = 80)]
    public string CarrierId { get; set; } = string.Empty;

    [SugarColumn(Length = 80)]
    public string LotId { get; set; } = string.Empty;

    /// <summary>E94 状态值：0 QUEUED、1 SELECTED、2 WAITINGFORSTART、3 EXECUTING、4 PAUSED、5 COMPLETED。</summary>
    public int State { get; set; }

    /// <summary>StartMethod：料到了直接开始。</summary>
    public bool AutoStart { get; set; }

    /// <summary>收下的 Stop / Abort：None / Stop / Abort。</summary>
    [SugarColumn(Length = 16)]
    public string Ending { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public DateTime? StartedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CompletedAt { get; set; }

    /// <summary>进 COMPLETED 走的转换号：10 正常、11 停止、12 中止；没完成为 0。</summary>
    public int CompletedBy { get; set; }

    /// <summary>删掉走的转换号：2 排队时删、13 完成后删；没删为 0。</summary>
    public int EndedBy { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? EndedAt { get; set; }

    /// <summary>设备重启时还没做完：开机后不接着跑，记成中止（CompletedBy 12）。</summary>
    public bool Restarted { get; set; }
}
