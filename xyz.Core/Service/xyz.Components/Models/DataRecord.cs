namespace xyz.Components.Models;

/// <summary>
/// 一个采样周期的一行：Time 是 UTC 毫秒（按采样周期对齐），Values 跟信号表一一对应，读不到的是 null。
/// </summary>
public sealed record DataRecord(long Time, double?[] Values);
