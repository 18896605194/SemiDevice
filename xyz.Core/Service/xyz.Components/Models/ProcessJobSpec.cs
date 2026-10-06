namespace xyz.Components.Models;

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
