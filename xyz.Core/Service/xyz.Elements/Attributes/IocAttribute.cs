using System;

namespace xyz.Elements.Attributes;

/// <summary>
/// 能从 IoC 容器按类型找到的接口：装配时元件只按打了这个标记的接口登记
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class IocAttribute : Attribute
{
}
