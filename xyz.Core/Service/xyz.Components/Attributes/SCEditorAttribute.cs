using System;

namespace xyz.Components.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SCEditorAttribute : Attribute
{

    public string? ConfigGroup { get; }

    public string? Description { get; }

    public string DefaultValue { get; }

    public bool Required { get; set; }

    public SCEditorAttribute(string defaultValue, string? configGroup = null, string? description = null)
    {
        DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
        ConfigGroup = configGroup;
        Description = description;
    }
}
