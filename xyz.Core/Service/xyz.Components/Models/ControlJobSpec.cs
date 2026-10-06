namespace xyz.Components.Models;

/// <summary>
/// 建一个 CJ 的请求（本地、Host 一样：Host 的 E94 Create Object，S14F9 翻过来就是它）：把已经建好的 PJ 按顺序收进来。
/// </summary>
public sealed record ControlJobSpec
{
    /// <summary>CtrlJobID。</summary>
    public required string Id { get; init; }

    /// <summary>ProcessingCtrlSpec 里的 PJ，按执行顺序。</summary>
    public required IReadOnlyList<string> ProcessJobs { get; init; }

    /// <summary>StartMethod：料到了直接开始（true），还是等 CJStart（false）。</summary>
    public bool AutoStart { get; init; }

    /// <summary>批次号（本地建 Job 时人填的）；Host 建的没有。</summary>
    public string? LotId { get; init; }
}
