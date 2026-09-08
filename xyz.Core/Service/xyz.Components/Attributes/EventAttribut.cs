namespace xyz.Components.Attributes;

/// <summary>
/// 声明组件的采集事件。公开实例字符串字段或属性的值作为稳定事件代码，
/// 与来源组件路径共同标识事件；对外 CEID 由设备接口表映射。
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
