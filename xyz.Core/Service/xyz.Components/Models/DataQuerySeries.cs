namespace xyz.Components.Models;

/// <summary>
/// 一条曲线的查询结果：Values 跟 <see cref="DataQueryResult.Times"/> 一一对应，null = 这一刻没有数据（曲线在这儿断开）。
/// Min / Max / Avg / Count 是查询时间段内原始数据的精确统计，抽不抽稀都一样。
/// </summary>
public sealed class DataQuerySeries
{
    public required string Name { get; init; }

    public required float?[] Values { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    public double? Avg { get; init; }

    public int Count { get; init; }
}
