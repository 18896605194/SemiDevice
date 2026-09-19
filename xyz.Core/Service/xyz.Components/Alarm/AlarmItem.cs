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

    /// <summary>本次报出时间，使用 UTC。</summary>
    public DateTimeOffset RaisedAt { get; internal init; }

    /// <summary>人工清除（Reset）时间；还在报时为 null。</summary>
    public DateTimeOffset? ClearedAt { get; internal set; }

    public bool IsActive => !ClearedAt.HasValue;

    internal AlarmItem Snapshot()
    {
        return (AlarmItem)MemberwiseClone();
    }
}
