using System;

namespace xyz.Components.Attributes;

/// <summary>
/// 组件特性，标记在组件上
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ComponentAttribute : Attribute
{
    public string? Key { get; }

    /// <summary>
    /// 组件用途说明（配置工具展示用）。
    /// </summary>
    public string? Description { get; }

    public ComponentAttribute(string? key = null, string? description = null)
    {
        Key = key;
        Description = description;
    }
}
