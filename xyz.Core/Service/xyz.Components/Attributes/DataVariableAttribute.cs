using xyz.Components.Enums;

namespace xyz.Components.Attributes;

/// <summary>
/// DV（SEMI E30 的 Data Variable）：只在报事件时才有值的数据，比如"哪个载具到了"的载具号、"哪个报警"的 ALID。
/// 标在组件的公开字符串字段（或属性）上，成员的值就是 DV 代码（跟 [Alarm]、[EventAttribut] 一个写法），
/// 全名 = 组件全路径.代码，开机由 DV 采集器编号（DvDefinitions.xml）。事件用 [EventAttribut] 的 Data 列出自己带哪些 DV，
/// 报事件时（ComponentBase.RaiseEvent）按代码给值。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DataVariableAttribute : Attribute
{
    public DataVariableAttribute(ValueFormat format, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Format = format;
        Description = description;
    }

    /// <summary>值的格式（Host 看 DV 名单时用；实际报的 SECS 格式按值走）。</summary>
    public ValueFormat Format { get; }

    public string Description { get; }

    /// <summary>单位（没有为空）。</summary>
    public string? Unit { get; set; }
}
