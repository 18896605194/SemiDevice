namespace xyz.Components.Events;

/// <summary>
/// 从组件采集到的事件声明，供事件目录和 CEID 映射使用。
/// </summary>
public sealed class EventDefinition
{
    public string SourcePath { get; init; } = string.Empty;

    public string EventCode { get; init; } = string.Empty;

    public string EventText { get; init; } = string.Empty;

    public string? Description { get; init; }
}
