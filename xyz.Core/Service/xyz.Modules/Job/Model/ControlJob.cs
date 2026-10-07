using xyz.Components.Enums;

namespace xyz.Modules;

public sealed class ControlJob
{
    public required string Id { get; init; }

    public required string LoadPort { get; init; }

    public string? CarrierId { get; init; }

    public string? LotId { get; init; }

    public ControlJobState State { get; internal set; } = ControlJobState.Created;

    /// <summary>中止前的状态，供 E94 在收尾期间继续上报原 ACTIVE 状态。</summary>
    internal ControlJobState StateBeforeAbort { get; set; }

    /// <summary>下面的 PJ，按执行顺序（ProcessingCtrlSpec 的顺序）。</summary>
    public List<ProcessJob> ProcessJobs { get; } = [];

    /// <summary>结束请求：Stop 等待完成；Abort 进入 Aborting，收尾后进入 Aborted。</summary>
    public ControlJobEnding Ending { get; set; } = ControlJobEnding.None;

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>开始执行（#5 / #7）的时刻。</summary>
    public DateTime? StartedAt { get; internal set; }

    /// <summary>完成或中止收尾的时刻。</summary>
    public DateTime? CompletedAt { get; internal set; }

    /// <summary>完成或中止收尾走的 E94 转换号（#10 正常、#11 停止、#12 中止）。</summary>
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

    /// <summary>已选中、等待启动、执行、暂停或中止收尾中的 CJ。</summary>
    public bool IsActive => State is ControlJobState.Selected or ControlJobState.WaitingForStart or ControlJobState.Executing or ControlJobState.Paused or ControlJobState.Aborting;
}

public enum ControlJobEnding
{
    None,
    Stop,
    Abort,
}
