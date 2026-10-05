namespace xyz.Modules;

/// <summary>
/// PJ 运行对象（SEMI E40 的 Process Job）：一组片走同一份配方快照。状态照 E40，只在 JobManager 的扫描线程里改。
/// 一片同时只属于一个没结束的 PJ；一个 PJ 只属于一个 CJ（Host 先建 PJ 再建 CJ 时，中间有一段还不归任何 CJ）。
/// </summary>
public sealed class ProcessJob
{
    /// <summary>PRJobID（E39 的 ObjID：ASCII，不能有 ? * ~ &gt; :）。</summary>
    public required string Id { get; init; }

    /// <summary>所属 CJ；还没被 CJ 收进去为 null。</summary>
    public ControlJob? ControlJob { get; set; }

    /// <summary>配方快照。</summary>
    public required JobRecipe Recipe { get; init; }

    /// <summary>PRProcessStart：准备好了直接开始（true），还是等 Start 命令（false）。</summary>
    public bool AutoStart { get; init; } = true;

    public PrJobState State { get; set; } = PrJobState.QueuedPooled;

    /// <summary>暂停前的执行子状态：恢复（#10）回到这里。</summary>
    public PrJobState ResumeState { get; set; } = PrJobState.Processing;

    /// <summary>这个 PJ 的片，按投片顺序（取片顺序在建 PJ 时排好）。</summary>
    public List<JobWafer> Wafers { get; } = [];

    /// <summary>谁建的。</summary>
    public JobCommandSource CreatedBy { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>开始执行（#4 / #5）的时刻。</summary>
    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    /// <summary>结束走的转换号（#7 正常、#16 中止、#17 停止、#18 排队时删）；没结束为 null。</summary>
    public int? EndedBy { get; set; }

    /// <summary>片位说不准、在途动作没确认：要人工恢复确认，解除前不往下走。</summary>
    public bool NeedsRecovery { get; set; }

    public bool IsEnded => EndedBy is not null;

    /// <summary>有片在机内（投了还没回来，或片位说不准）。</summary>
    public bool HasWafersInMachine => Wafers.Any(wafer => wafer.IsInMachine);

    /// <summary>有在途的搬运或加工。</summary>
    public bool HasInFlight => Wafers.Any(wafer => wafer.HasInFlight);

    /// <summary>还有没投的片。</summary>
    public bool HasWaitingWafers => Wafers.Any(wafer => wafer.Phase == JobWaferPhase.Waiting);
}
