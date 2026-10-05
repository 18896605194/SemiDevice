namespace xyz.Modules;

/// <summary>
/// CJ 要结束的方式：Stop / Abort 命令收下之后，CJ 的状态值不变，等下面的 PJ 都结束再走 #11 / #12。
/// </summary>
public enum CtrlJobEnding
{
    None,
    Stop,
    Abort,
}

/// <summary>
/// CJ 运行对象（SEMI E94 的 Control Job）：一个载具（一个 LoadPort）上这一批片怎么跑，下面按顺序挂几个 PJ。
/// 状态照 E94，只在 JobManager 的扫描线程里改。
/// </summary>
public sealed class ControlJob
{
    /// <summary>CtrlJobID（E39 的 ObjID）。本地建的默认用 LotID。</summary>
    public required string Id { get; init; }

    /// <summary>来源 LoadPort（载具在哪）。</summary>
    public required string LoadPort { get; init; }

    /// <summary>载具号（建 CJ 时 LoadPort 读到的；没读到为空）。</summary>
    public string? CarrierId { get; init; }

    /// <summary>批次号。</summary>
    public string? LotId { get; init; }

    /// <summary>建 CJ 时 LoadPort 上那个载具对象的标识：载具拿走（或换了一个）之后，完成的 CJ 就可以删了（#13）。</summary>
    public Guid? CarrierInstance { get; init; }

    /// <summary>StartMethod：料到了直接开始（true），还是等 Start 命令（false）。</summary>
    public bool AutoStart { get; init; }

    public CtrlJobState State { get; set; } = CtrlJobState.Queued;

    /// <summary>下面的 PJ，按执行顺序（ProcessingCtrlSpec 的顺序）。</summary>
    public List<ProcessJob> ProcessJobs { get; } = [];

    /// <summary>收下的 Stop / Abort：等 PJ 都结束再进 COMPLETED。</summary>
    public CtrlJobEnding Ending { get; set; } = CtrlJobEnding.None;

    public JobCommandSource CreatedBy { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>开始执行（#5 / #7）的时刻。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>进 COMPLETED 的时刻。</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>进 COMPLETED 走的转换号（#10 正常、#11 停止、#12 中止）。</summary>
    public int? CompletedBy { get; set; }

    /// <summary>删掉走的转换号（#2 排队时删、#13 完成后删）；没删为 null。</summary>
    public int? EndedBy { get; set; }

    public DateTime? EndedAt { get; set; }

    public bool IsEnded => EndedBy is not null;

    /// <summary>下面有 PJ 要人工恢复确认。</summary>
    public bool NeedsRecovery => ProcessJobs.Any(job => job.NeedsRecovery);
}
