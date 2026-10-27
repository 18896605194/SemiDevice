using System;

namespace xyz.Elements.Attributes;

/// <summary>
/// 元件特性，标记在元件上
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ElementAttribute : Attribute
{
    public string? Key { get; }

    public string? Description { get; }

    public ElementAttribute(string? key = null, string? description = null)
    {
        Key = key;
        Description = description;
    }
}
