using System;

namespace xyz.Elements.Attributes;

/// <summary>
/// 装机参数
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MachineAttribute : Attribute
{

    public string? ConfigGroup { get; }

    public string? Description { get; }

    public string DefaultValue { get; }

    public bool Required { get; set; }

    public MachineAttribute(string defaultValue, string? configGroup = null, string? description = null)
    {
        DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
        ConfigGroup = configGroup;
        Description = description;
    }
}
