using xyz.Components.Enums;

namespace xyz.Modules;

public sealed class ControlJob
{
    public required string Id { get; init; }

    public required string LoadPort { get; init; }

    public string? CarrierId { get; init; }

    public string? LotId { get; init; }

    /// <summary>建 CJ 时 LoadPort 上那个载具对象的标识：载具拿走（或换了一个）之后，完成的 CJ 就可以删了（#13）。</summary>
    public Guid? CarrierInstance { get; init; }

    /// <summary>StartMethod：料到了直接开始（true），还是等 Start 命令（false）。</summary>
    public bool AutoStart { get; init; }

    public ControlJobState State { get; internal set; } = ControlJobState.Queued;

    /// <summary>下面的 PJ，按执行顺序（ProcessingCtrlSpec 的顺序）。</summary>
    public List<ProcessJob> ProcessJobs { get; } = [];

    /// <summary>收下的 Stop / Abort：CJ 没有停止中、中止中的状态，状态值不变，等 PJ 都结束再进 COMPLETED（#11 / #12）。</summary>
    public ControlJobEnding Ending { get; set; } = ControlJobEnding.None;

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>开始执行（#5 / #7）的时刻。</summary>
    public DateTime? StartedAt { get; internal set; }

    /// <summary>进 COMPLETED 的时刻。</summary>
    public DateTime? CompletedAt { get; internal set; }

    /// <summary>进 COMPLETED 走的转换号（#10 正常、#11 停止、#12 中止）。</summary>
    public int? CompletedBy { get; internal set; }

    /// <summary>删掉走的转换号（#2 排队时删、#13 完成后删）；没删为 null。</summary>
    public int? EndedBy { get; internal set; }

    public DateTime? EndedAt { get; internal set; }

    /// <summary>库里 control_job 表那一行的 Id（第一次写进去之后才有，0 = 还没写）。只有 Job 管理的存盘线程读写。</summary>
    internal long RowId { get; set; }

    public bool IsEnded => EndedBy is not null;

    /// <summary>
    /// 能不能启动下面新的 PJ：在执行、没收 Stop / Abort。CJ 暂停（E94）就是这里关上——在跑的 PJ 不受影响。
    /// </summary>
    public bool CanStartProcessJobs => State == ControlJobState.Executing && Ending == ControlJobEnding.None;

    /// <summary>在 ACTIVE 超状态里（选中、等启动、执行、暂停）：算"在跑的 CJ"。</summary>
    public bool IsActive => State is ControlJobState.Selected or ControlJobState.WaitingForStart or ControlJobState.Executing or ControlJobState.Paused;
}

public enum ControlJobEnding
{
    None,
    Stop,
    Abort,
}
