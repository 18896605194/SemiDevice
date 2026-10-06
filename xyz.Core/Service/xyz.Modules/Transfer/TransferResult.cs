namespace xyz.Modules;

/// <summary>
/// 一张搬运单怎么结束的。
/// </summary>
public enum TransferOutcome
{
    /// <summary>搬完了：片在目标槽，两个站点都收尾回到待命。</summary>
    Completed,

    /// <summary>没搬成（站点等不到、准备失败、取放失败）。</summary>
    Failed,

    /// <summary>跑到一半被中止（Job 中止、整机停止、人工撤单）。</summary>
    Aborted,

    /// <summary>还没开始就被撤了：片没动过。</summary>
    Cancelled,
}

/// <summary>
/// 一张搬运单的最终结果：设备动作、站点收尾、晶圆账都做完了才发。
/// <see cref="NeedsRecovery"/> 为 true 时片在哪说不准（动过手才失败），这张单的锁不放，等人工确认后调 ReleaseHold。
/// </summary>
public sealed record TransferResult
{
    /// <summary>搬运单号。</summary>
    public long Id { get; init; }

    public TransferOrigin Origin { get; init; }

    /// <summary>下单的 Job（PJ 名）；手动单为空。</summary>
    public string? Owner { get; init; }

    /// <summary>搬的那一片（晶圆账内部标识）。</summary>
    public Guid WaferId { get; init; }

    public string Source { get; init; } = string.Empty;

    public int SourceSlot { get; init; }

    public string Target { get; init; } = string.Empty;

    public int TargetSlot { get; init; }

    public string Robot { get; init; } = string.Empty;

    public int Arm { get; init; }

    public TransferOutcome Outcome { get; init; }

    /// <summary>没搬成时的错误码（界面按码查语言包）；搬成了为空。</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>错误码参数。</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>片位说不准，锁留着等人工确认。</summary>
    public bool NeedsRecovery { get; init; }

    /// <summary>取片做完了（片到过机械手手上）：没搬成时用它分出错的是取片还是放片。只放片的单（源是机械手）为 false。</summary>
    public bool Picked { get; init; }

    public DateTime CreatedAt { get; init; }

    /// <summary>开始执行的时刻；还没开始就被撤的为 null。</summary>
    public DateTime? StartedAt { get; init; }

    public DateTime EndedAt { get; init; }

    public bool IsSuccess => Outcome == TransferOutcome.Completed;
}
