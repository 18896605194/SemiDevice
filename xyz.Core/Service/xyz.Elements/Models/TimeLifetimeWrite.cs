namespace xyz.Elements.Models;

/// <summary>
/// 时间寿命的一次写库：Clear 是清零（起算时间改成 StartedAt、秒数归 0），不然给通着的秒数加 AddSeconds
/// </summary>
public readonly record struct TimeLifetimeWrite(bool Clear, DateTime StartedAt, long AddSeconds);
