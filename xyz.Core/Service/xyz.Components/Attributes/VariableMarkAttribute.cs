using System;
using xyz.Components.Enums;

namespace xyz.Components.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class VariableMarkAttribute : Attribute
{
    /// <summary>
    /// 采集名覆盖（默认属性名；用于成员名与既有属性冲突时保住对外 SV 名）。EC/DV 忽略。
    /// </summary>
    public string? Name { get; set; }

    public VariableType Type { get; }

    public ValueFormat Format { get; }

    public string? Unit { get; }

    public string? Min { get; }

    public string? Max { get; }

    public string? Default { get; }

    public string? Description { get; }

    public string? Options { get; set; }

    public bool Visible { get; set; } = true;

    public VariableMarkAttribute(VariableType type, ValueFormat format,
        string? unit = null, string? min = null, string? max = null,
        string? @default = null, string? description = null)
    {
        Type = type;
        Format = format;
        Unit = unit;
        Min = min;
        Max = max;
        Default = @default;
        Description = description;
    }
}
