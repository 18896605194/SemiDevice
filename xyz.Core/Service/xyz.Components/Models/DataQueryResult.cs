namespace xyz.Components.Models;

/// <summary>
/// 按名字、按时间段查出来的曲线：所有曲线共用一条时间轴（UTC 毫秒）。
/// </summary>
public sealed class DataQueryResult
{
    public static readonly DataQueryResult Empty = new() { Times = [], Series = [] };

    public required long[] Times { get; init; }

    public required IReadOnlyList<DataQuerySeries> Series { get; init; }

    /// <summary>
    /// 时间段太长、点数超过上限时抽稀过：每段（BucketMs 宽）只留最小、最大两个点，按出现的先后排——峰谷不丢，曲线形状不走样。
    /// </summary>
    public bool IsDecimated { get; init; }

    public long BucketMs { get; init; }
}
