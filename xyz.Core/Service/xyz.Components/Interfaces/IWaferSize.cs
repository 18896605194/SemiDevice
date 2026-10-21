using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 整机晶圆尺寸（8 / 12 寸）
/// </summary>
public interface IWaferSize
{
    /// <summary>
    /// 现在做几寸片
    /// </summary>
    int Size { get; }

    /// <summary>
    /// 切尺寸，写回 sc.xml
    /// </summary>
    HandleResult SetSize(int size);

    /// <summary>
    /// 切了尺寸（带新尺寸）
    /// </summary>
    event Action<int>? Changed;
}
