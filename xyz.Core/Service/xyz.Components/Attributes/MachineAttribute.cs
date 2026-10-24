using System;

namespace xyz.Components.Attributes;

/// <summary>
/// 装机参数：属性的值从 machine.xml 组件节点下同名的 Value 灌（装机、接线、结构性的，改了重启生效；现场在线调的用 EC）
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
