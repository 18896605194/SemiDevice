namespace xyz.Components.Interfaces;

/// <summary>
/// 虚拟 DO：程序写、别的组件按点号读，只在内存里，不下发 PLC
/// </summary>
public interface IVirtualDo : IDeviceComponent
{
    /// <summary>
    /// 点号（5000~5999）
    /// </summary>
    int Index { get; }

    /// <summary>
    /// 现在是通还是断
    /// </summary>
    bool Value { get; }

    /// <summary>
    /// 写通 / 断
    /// </summary>
    void Write(bool on);
}
