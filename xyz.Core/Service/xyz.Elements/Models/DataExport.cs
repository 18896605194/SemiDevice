namespace xyz.Elements.Models;

/// <summary>
/// 时间，点位名，点位value
/// </summary>
public sealed class DataExport
{
    public required long[] Times { get; init; }

    public required IReadOnlyList<string> Names { get; init; }

    public required double?[][] Values { get; init; }
}
