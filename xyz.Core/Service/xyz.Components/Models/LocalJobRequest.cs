namespace xyz.Components.Models;

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
