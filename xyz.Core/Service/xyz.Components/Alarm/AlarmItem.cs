namespace xyz.Components.Alarm;

/// <summary>
/// 一次报警的运行时快照。与组件上的 AlarmAttribute 定义分开保存。
/// </summary>
public sealed class AlarmItem
{
    public string SourcePath { get; internal init; } = string.Empty;

    public string AlarmCode { get; internal init; } = string.Empty;

    public string AlarmText { get; internal init; } = string.Empty;

    public AlarmCategory Category { get; internal init; }

    public AlarmLevel Level { get; internal init; }

    public string? Description { get; internal init; }

    public string? Solution { get; internal init; }

    /// <summary>本次触发时间，使用 UTC。</summary>
    public DateTimeOffset RaisedAt { get; internal init; }

    /// <summary>人工确认时间，未确认时为 null。</summary>
    public DateTimeOffset? AcknowledgedAt { get; internal set; }

    /// <summary>故障恢复时间，仍然活动时为 null。</summary>
    public DateTimeOffset? ClearedAt { get; internal set; }

    public bool IsAcknowledged => AcknowledgedAt.HasValue;

    public bool IsActive => !ClearedAt.HasValue;

    internal AlarmItem Snapshot()
    {
        return (AlarmItem)MemberwiseClone();
    }
}
