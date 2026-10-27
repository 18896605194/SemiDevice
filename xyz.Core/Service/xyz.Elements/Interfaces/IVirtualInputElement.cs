using xyz.Elements.Models;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 虚拟输入点（按表达式算出来的 DI / AI）
/// </summary>
public interface IVirtualInputElement
{
    /// <summary>
    /// 点号
    /// </summary>
    int Index { get; }

    /// <summary>
    /// 元件全路径
    /// </summary>
    string FullPath { get; }

    /// <summary>
    /// 点的类型（DI / AI）
    /// </summary>
    internal string PointType { get; }

    /// <summary>
    /// 编好的表达式
    /// </summary>
    internal IoExpression Compiled { get; }

    /// <summary>
    /// 挂到登记表上
    /// </summary>
    internal void Bind(VirtualInputRegistry registry);

    /// <summary>
    /// 写算出来的值
    /// </summary>
    internal void SetValue(double? value);
}
