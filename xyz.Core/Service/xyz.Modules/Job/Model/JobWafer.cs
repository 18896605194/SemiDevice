namespace xyz.Modules;

/// <summary>
/// 一片在路线上走到哪了。
/// </summary>
public enum JobWaferPhase
{
    /// <summary>还在来源槽里，没投。</summary>
    Waiting,

    /// <summary>搬运单在途（去下一站，或回片）。</summary>
    Moving,

    /// <summary>到了要加工的站点，等加工。</summary>
    Arrived,

    /// <summary>加工中。</summary>
    Processing,

    /// <summary>这一站做完了（加工完，或不用加工的站点到了），等去下一站。</summary>
    Processed,

    /// <summary>回到回片槽，这一片结束了。</summary>
    Done,

    /// <summary>不在该在的地方（被人改了账、载具被拿走、重新 Mapping），也不在搬运里：等人工确认。</summary>
    Lost,
}

/// <summary>
/// 一片的最终结果（PJ 结束时定下来）。
/// </summary>
public enum JobWaferOutcome
{
    /// <summary>还没结束。</summary>
    None,

    /// <summary>每一步都做完、回片了。</summary>
    Completed,

    /// <summary>有一步加工没做成，没做后面的步骤直接回片了。</summary>
    Failed,

    /// <summary>没投（停止、中止、取消时还在来源槽里）。</summary>
    NotRun,

    /// <summary>中止时还在机内，停在哪就在哪，等人工收回。</summary>
    Aborted,
}

/// <summary>
/// 一片在一站的结果（加工了的站点才记）。
/// </summary>
public sealed record JobStepResult
{
    /// <summary>路线上第几步（从 0 开始）。</summary>
    public int Step { get; init; }

    public string Station { get; init; } = string.Empty;

    public string Recipe { get; init; } = string.Empty;

    /// <summary>用的工艺配方版本（快照的）。</summary>
    public int RecipeRevision { get; init; }

    public bool Success { get; init; }

    /// <summary>没做成时的错误码。</summary>
    public string Code { get; init; } = string.Empty;

    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>加工是模拟的（设备驱动还没接，计时就算做完）。</summary>
    public bool Simulated { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime EndedAt { get; init; }
}

/// <summary>
/// 在等什么（给界面看的原因，错误码 + 参数，界面按码查语言包）。
/// </summary>
public sealed record JobWait(string Code, IReadOnlyList<string> Args)
{
    public static JobWait Of(string code, params string[] args)
    {
        return new JobWait(code, args);
    }
}

/// <summary>
/// PJ 里的一片：身份（晶圆账的内部标识）、从哪来回哪去、走到路线的第几步、在途的搬运或加工、每一步的结果。
/// 只在 JobManager 的扫描线程里改。
/// </summary>
public sealed class JobWafer
{
    /// <summary>晶圆账的内部标识：片号改了、片挪了都认得；整篮重新 Mapping 会换新标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>片号（建 PJ 时账上的，显示用）。</summary>
    public required string Name { get; init; }

    public required string SourcePort { get; init; }

    public required int SourceSlot { get; init; }

    /// <summary>回片 LoadPort（建 PJ 时按规则定好）。</summary>
    public required string ReturnPort { get; init; }

    public required int ReturnSlot { get; init; }

    /// <summary>路线上第几步：-1 = 还在来源槽；0 ~ 步数-1 = 中间那一站；= 步数 表示回片。</summary>
    public int Step { get; set; } = -1;

    public JobWaferPhase Phase { get; set; } = JobWaferPhase.Waiting;

    /// <summary>到站后所在的站点和槽（Arrived / Processing / Processed）；回片后是回片槽。</summary>
    public string? Station { get; set; }

    public int Slot { get; set; }

    /// <summary>在途的搬运单号。</summary>
    public long? TransferId { get; set; }

    /// <summary>搬运单的目标（在途时）。</summary>
    public string? MovingTo { get; set; }

    public int MovingToSlot { get; set; }

    /// <summary>搬运前的步号和阶段：没动过手就失败时退回去。</summary>
    public int FromStep { get; set; } = -1;

    public JobWaferPhase FromPhase { get; set; } = JobWaferPhase.Waiting;

    /// <summary>在途的加工操作（加工中才有）。</summary>
    public ModuleOperation? Process { get; set; }

    /// <summary>加工开始的时刻。</summary>
    public DateTime ProcessStartedAt { get; set; }

    /// <summary>有一步加工没做成：后面的步骤不做了，直接回片。</summary>
    public bool Failed { get; set; }

    /// <summary>加工了的每一站的结果。</summary>
    public List<JobStepResult> Results { get; } = [];

    public JobWaferOutcome Outcome { get; set; } = JobWaferOutcome.None;

    /// <summary>这一拍在等什么；不在等为 null。</summary>
    public JobWait? Wait { get; set; }

    /// <summary>有在途的动作（搬运或加工）。</summary>
    public bool HasInFlight => TransferId is not null || Process is not null;

    /// <summary>
    /// 加工这件事做完了（还没回片也算）：回片了、在回片路上、最后一站做完了，或者某一站没做成（不做后面的了）。
    /// 还没投、片位说不准、还在半路上的都不算。
    /// </summary>
    public bool IsProcessFinished(int stepCount)
    {
        return Phase switch
        {
            JobWaferPhase.Done => true,
            JobWaferPhase.Moving => Step >= stepCount,
            JobWaferPhase.Processed => Failed || Step >= stepCount - 1,
            _ => false,
        };
    }

    /// <summary>在机内：投了、还没回片（片位说不准的也算）。</summary>
    public bool IsInMachine => Phase is JobWaferPhase.Moving or JobWaferPhase.Arrived or JobWaferPhase.Processing
        or JobWaferPhase.Processed or JobWaferPhase.Lost;
}
