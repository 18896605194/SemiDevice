using System;

namespace xyz.Components.Attributes;

/// <summary>
/// 标记一个类为可由组件工厂实例化的组件，与 GR 项目的 [Component] 用法一致。
/// Key 全局唯一；省略时注册键自动取 CLR 全名。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ComponentAttribute : Attribute
{
    /// <summary>
    /// 组件类型键，全局唯一。null/空 = 用 CLR 全名注册。
    /// </summary>
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
