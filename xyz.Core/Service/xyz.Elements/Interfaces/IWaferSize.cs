using xyz.Shared.Dtos;
using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 整机晶圆尺寸（8 / 12 寸）
/// </summary>
[Ioc]
public interface IWaferSize
{

    int Size { get; }

    /// <summary>
    /// 切尺寸，写回 machine.xml
    /// </summary>
    HandleResult SetSize(int size);

    /// <summary>
    /// 切换尺寸的事件
    /// </summary>
    event Action<int>? Changed;
}
