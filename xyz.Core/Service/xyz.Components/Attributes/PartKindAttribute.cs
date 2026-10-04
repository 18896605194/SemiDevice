using System;

namespace xyz.Components.Attributes;

/// <summary>
/// 部件种类：标在组件类上（派生类继承），手动页按它把部件放到对应的地方——轴一个页签、双作用气缸进气缸表……
/// 模块只把标了种类的组件推给界面。新硬件要上手动页：给它的类标一个种类，再在属性上标 <see cref="LiveValueAttribute"/>、
/// 方法上标 <see cref="ManualActionAttribute"/>，推送和动作接口都不用改。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PartKindAttribute : Attribute
{
    /// <summary>
    /// 种类名，如 "Axis"、"TwoState"；界面按它选模板，界面不认识的种类只推数据、不显示。
    /// </summary>
    public string Kind { get; }

    public PartKindAttribute(string kind)
    {
        Kind = kind;
    }
}
