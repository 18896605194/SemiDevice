using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 虚拟 IO：程序写的虚拟 DO，按点号读写，用法跟 IIo 一样
/// </summary>
[Ioc]
public interface IVirtualIo
{
    /// <summary>
    /// 读一个虚拟 DO，没有这个点号返回 false
    /// </summary>
    bool TryReadDo(int index, out bool on);

    /// <summary>
    /// 写一个虚拟 DO，没有这个点号返回 false
    /// </summary>
    bool WriteDo(int index, bool on);

    /// <summary>
    /// 把一个虚拟 DO 翻一下（通变断、断变通），没有这个点号返回 false；计数就用它
    /// </summary>
    bool ToggleDo(int index);
}
