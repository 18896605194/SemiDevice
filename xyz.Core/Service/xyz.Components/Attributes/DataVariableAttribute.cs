using xyz.Components.Enums;

namespace xyz.Components.Attributes;

/// <summary>
/// DV（SEMI E30 的 Data Variable）
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
