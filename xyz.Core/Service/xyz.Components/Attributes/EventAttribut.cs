namespace xyz.Components.Attributes;

/// <summary>
/// 采集事件 特性
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class EventAttribut : Attribute
{
    public string EventText { get; }

    public string? Description { get; set; }

    public EventAttribut(string eventText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventText);
        EventText = eventText;
    }
}
