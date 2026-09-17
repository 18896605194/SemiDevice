namespace xyz.Shared.Dtos;

/// <summary>
/// 报警契约：报警触发、确认、恢复各推一条，界面据此刷新报警列表。
/// </summary>
public class AlarmDto
{
    public const string EventToken = "Alarm";

    /// <summary>报警来源的组件路径，如 Robot1、LoadPort1.RFID。</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>报警代码，同一来源内唯一。</summary>
    public string Code { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>分类（AlarmCategory 名字）。报警枚举在组件层，契约层不引用组件层，所以用字符串带。</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>等级（AlarmLevel 名字）。</summary>
    public string Level { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>处理建议，界面直接显示给操作员。</summary>
    public string Solution { get; set; } = string.Empty;

    public DateTime RaisedAt { get; set; }

    /// <summary>人工确认时刻；未确认为 null。</summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>故障恢复时刻；仍在报为 null。</summary>
    public DateTime? ClearedAt { get; set; }

    /// <summary>还在报（没恢复）。确认过但没恢复的仍算在报。</summary>
    public bool IsActive => !ClearedAt.HasValue;

    public bool IsAcknowledged => AcknowledgedAt.HasValue;
}
