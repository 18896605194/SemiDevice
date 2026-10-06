namespace xyz.Components.Models;

/// <summary>
/// 建一个 PJ 的请求（本地、Host 一样：Host 的 E40 PRJobCreate，S16F11 / S16F15 翻过来就是它）：哪个载具上哪些槽、用哪个流程配方。
/// 先建 PJ、再建 CJ 把它收进去；PJ 在被 CJ 收进去之前一直排着（QUEUED/POOLED）。
/// </summary>
public sealed record ProcessJobSpec
{
    /// <summary>PRJobID。</summary>
    public required string Id { get; init; }

    /// <summary>LoadPort（本地建知道是哪个口，载具号可能没读到）；给了就按它找，不给按载具号找。</summary>
    public string? LoadPort { get; init; }

    /// <summary>载具号（Host 给的）：按它找载具在哪个 LoadPort 上（现在要求载具已经在 LoadPort 上）。</summary>
    public string CarrierId { get; init; } = string.Empty;

    /// <summary>要做的槽（槽号从 1 开始），按给的先后投片。</summary>
    public required IReadOnlyList<int> Slots { get; init; }

    /// <summary>流程配方名（Host 的 RecID）。</summary>
    public required string Sequence { get; init; }

    /// <summary>PRProcessStart：准备好了直接开始（true），还是等 PJ Start（false）。</summary>
    public bool AutoStart { get; init; } = true;
}
