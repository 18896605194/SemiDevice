namespace xyz.Elements.Interfaces;

/// <summary>
/// 虚拟 DO：挂在虚拟 IO 下面，声明一个点号
/// </summary>
public interface IVirtualDo : IDevice
{
    /// <summary>
    /// 点号（5000~5999）
    /// </summary>
    int Index { get; }
}
