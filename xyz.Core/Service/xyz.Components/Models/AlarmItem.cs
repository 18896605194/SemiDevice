using xyz.Components.Enums;

namespace xyz.Components.Models;

/// <summary>
/// 报警消息类
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

    /// <summary>
    /// 触发时间
    /// </summary>
    public DateTimeOffset RaisedAt { get; internal init; }

    /// <summary>
    /// reset 时间
    /// </summary>
    public DateTimeOffset? ClearedAt { get; internal set; }

    /// <summary>
    /// 是否是活跃的
    /// </summary>
    public bool IsActive => !ClearedAt.HasValue;

    internal AlarmItem Snapshot()
    {
        return (AlarmItem)MemberwiseClone();
    }
}
