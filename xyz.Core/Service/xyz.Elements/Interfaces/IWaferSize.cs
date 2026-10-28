using xyz.Shared.Dtos;
using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 整机晶圆尺寸（8 / 12 寸）
/// </summary>
[Ioc]
public interface IWaferSize
{
    /// <summary>
    /// 现在做几寸片
    /// </summary>
    int Size { get; }

    /// <summary>
    /// 切尺寸，写回 machine.xml
    /// </summary>
    HandleResult SetSize(int size);

    /// <summary>
    /// 切了尺寸（带新尺寸）
    /// </summary>
    event Action<int>? Changed;
}
