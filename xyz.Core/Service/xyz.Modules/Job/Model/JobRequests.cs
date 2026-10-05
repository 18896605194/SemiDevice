namespace xyz.Modules;

/// <summary>
/// 命令的受理结果：收下了带 Job 名；没收带错误码和参数（界面按码查语言包，Host 那边翻成 S16F6 / S16F28 的错误）。
/// 受理只说明命令生效了（状态已经转了，或者 Stop / Abort 已经开始做），整个 Job 什么时候结束看状态推送。
/// </summary>
public sealed record JobCommandResult(bool Accepted, string Code, IReadOnlyList<string> Args, string JobId)
{
    public static JobCommandResult Ok(string jobId)
    {
        return new JobCommandResult(true, string.Empty, [], jobId);
    }

    public static JobCommandResult Reject(string code, params string[] args)
    {
        return new JobCommandResult(false, code, args, string.Empty);
    }
}

/// <summary>
/// 本地建 Job（主界面一个 LoadPort 页签）：每槽一个流程配方，相同流程配方的槽分成一个 PJ，整篮一个 CJ。
/// </summary>
public sealed record LocalJobRequest
{
    public required string LoadPort { get; init; }

    /// <summary>批次号，也当 CJ 的名字；空就自动起名（CJ-LoadPort-时间）。</summary>
    public string? LotId { get; init; }

    /// <summary>槽号 → 流程配方名。</summary>
    public required IReadOnlyDictionary<int, string> SlotSequences { get; init; }

    /// <summary>CJ 的 StartMethod：料到了直接开始（true），还是等"启动 Job"（false）。</summary>
    public bool AutoStart { get; init; }

    /// <summary>请求号：同一个请求重发回同一个结果，不会建两份。</summary>
    public string? RequestId { get; init; }

    public string Operator { get; init; } = string.Empty;
}

/// <summary>
/// 建一个 PJ（Host 的 E40 PRJobCreate：S16F11 / S16F15 翻过来就是它）：载具上哪些槽、用哪个流程配方。
/// 先建 PJ、再建 CJ 把它收进去；PJ 在被 CJ 收进去之前一直排着（QUEUED/POOLED）。
/// </summary>
public sealed record ProcessJobSpec
{
    /// <summary>PRJobID。</summary>
    public required string Id { get; init; }

    /// <summary>载具号：找它在哪个 LoadPort 上（现在要求载具已经在 LoadPort 上）。</summary>
    public required string CarrierId { get; init; }

    /// <summary>要做的槽（槽号从 1 开始），按给的先后投片。</summary>
    public required IReadOnlyList<int> Slots { get; init; }

    /// <summary>流程配方名（Host 的 RecID）。</summary>
    public required string Sequence { get; init; }

    /// <summary>PRProcessStart：准备好了直接开始（true），还是等 PJ Start（false）。</summary>
    public bool AutoStart { get; init; } = true;

    public string? RequestId { get; init; }
}

/// <summary>
/// 建一个 CJ（Host 的 E94 Create Object：S14F9 翻过来就是它）：把已经建好的 PJ 按顺序收进来。
/// </summary>
public sealed record ControlJobSpec
{
    /// <summary>CtrlJobID。</summary>
    public required string Id { get; init; }

    /// <summary>ProcessingCtrlSpec 里的 PJ，按执行顺序。</summary>
    public required IReadOnlyList<string> ProcessJobs { get; init; }

    /// <summary>StartMethod：料到了直接开始（true），还是等 CJStart（false）。</summary>
    public bool AutoStart { get; init; }

    public string? RequestId { get; init; }
}
