using System;

namespace xyz.Client.Modules;

/// <summary>
/// 标记一个类为客户端机型模块，壳启动时扫描 DLL 并按此特性发现。
/// Key 全局唯一（一般用机型号），省略时用 CLR 全名。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ClientModuleAttribute : Attribute
{
    /// <summary>
    /// 机型模块键，全局唯一。null/空 = 用 CLR 全名。
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// 机型模块用途说明。
    /// </summary>
    public string? Description { get; }

    public ClientModuleAttribute(string? key = null, string? description = null)
    {
        Key = key;
        Description = description;
    }
}
