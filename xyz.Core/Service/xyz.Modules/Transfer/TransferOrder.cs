namespace xyz.Modules;

/// <summary>
/// 搬运管理里的一张单（受理时解析好站点、机械手、手臂、片），外面看到的是 <see cref="TransferResult"/>。
/// 字段在搬运管理的锁里或扫描线程上改；中止标记任意线程可落。
/// </summary>
internal sealed class TransferOrder
{
    public required long Id { get; init; }

    public required TransferOrigin Origin { get; init; }

    public string? Owner { get; init; }

    public required Guid WaferId { get; init; }

    /// <summary>片号（受理时账上的，日志和结果里给人看）。</summary>
    public required string WaferName { get; init; }

    public required IRobot Robot { get; init; }

    /// <summary>源站点；片已经在机械手手上（只放片）时为 null，源就是机械手、源槽是手指号。</summary>
    public ITransferStation? Source { get; init; }

    /// <summary>源的名字（站点名；只放片时是机械手名）：锁、日志、结果都按它。</summary>
    public required string SourceName { get; init; }

    public required int SourceSlot { get; init; }

    public required ITransferStation Target { get; init; }

    public required int TargetSlot { get; init; }

    public required int Arm { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? StartedAt { get; set; }

    /// <summary>开始执行后才有。</summary>
    public TransferRoutine? Routine { get; set; }

    private volatile bool _abortRequested;

    /// <summary>要撤这张在跑的单：扫描线程下一拍撤。</summary>
    public bool AbortRequested
    {
        get { return _abortRequested; }
        set { _abortRequested = value; }
    }

    /// <summary>撤单原因（进日志）。</summary>
    public string AbortReason { get; set; } = string.Empty;

    /// <summary>最终结果（结束时落）。续体异步跑，免得在搬运管理的扫描线程上执行调用方的代码。</summary>
    public TaskCompletionSource<TransferResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>"源.槽 → 目标.槽（机械手 手 N）"，日志用。</summary>
    public string Describe()
    {
        return $"{SourceName}.{SourceSlot:00} → {Target.Name}.{TargetSlot:00}（{Robot.Name} 手 {Arm}）";
    }

    public TransferResult ToResult(TransferOutcome outcome, string code, IReadOnlyList<string> args, bool needsRecovery, bool picked)
    {
        return new TransferResult
        {
            Id = Id,
            Origin = Origin,
            Owner = Owner,
            WaferId = WaferId,
            Source = SourceName,
            SourceSlot = SourceSlot,
            Target = Target.Name,
            TargetSlot = TargetSlot,
            Robot = Robot.Name,
            Arm = Arm,
            Outcome = outcome,
            Code = outcome == TransferOutcome.Completed ? string.Empty : code,
            Args = outcome == TransferOutcome.Completed ? [] : args.ToList(),
            NeedsRecovery = needsRecovery,
            Picked = picked,
            CreatedAt = CreatedAt,
            StartedAt = StartedAt,
            EndedAt = DateTime.Now,
        };
    }
}
