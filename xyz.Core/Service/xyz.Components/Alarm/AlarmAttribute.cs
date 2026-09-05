using System;

namespace xyz.Components.Alarm;

/// <summary>
/// 标记一个组件报警（字段或属性），与 GR 项目的 [Alarm] 用法一致。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AlarmAttribute : Attribute
{
    /// <summary>
    /// 报警文本。
    /// </summary>
    public string AlarmText { get; }

    /// <summary>
    /// 报警分类。
    /// </summary>
    public AlarmCategory Category { get; }

    /// <summary>
    /// 报警等级，默认 Alarm2。
    /// </summary>
    public AlarmLevel AlarmLevel { get; set; } = AlarmLevel.Alarm2;

    /// <summary>
    /// 详细描述。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 处理建议。
    /// </summary>
    public string? Solution { get; set; }

    public AlarmAttribute(string alarmText, AlarmCategory category)
    {
        AlarmText = alarmText;
        Category = category;
    }
}
