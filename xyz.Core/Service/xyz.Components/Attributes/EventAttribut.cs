namespace xyz.Components.Attributes;

/// <summary>
/// 事件（SEMI E30 的 Collection Event）
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class EventAttribut : Attribute
{
    public string EventText { get; }

    public string? Description { get; set; }

    /// <summary>
    /// 这个事件带的 DV：同一个组件上 [DataVariable] 的代码，按先后。Host 问事件名单（S1F23）时报这些，
    /// 报事件时这些 DV 有值（RaiseEvent 给的），报告里引用别的 DV 时报空。
    /// </summary>
    public string[] Data { get; set; } = [];

    public EventAttribut(string eventText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventText);
        EventText = eventText;
    }
}
