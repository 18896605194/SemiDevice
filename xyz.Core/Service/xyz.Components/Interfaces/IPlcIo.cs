namespace xyz.Components.Interfaces;

/// <summary>
/// PLC 的 DI/DO/AI/AO 点位读写
/// </summary>
public interface IPlcIo
{
    /// <summary>
    /// 通讯是否连着
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 读一个 DI，读不到返回 false
    /// </summary>
    bool TryReadDi(int index, out bool on);

    /// <summary>
    /// 回读一个 DO，读不到返回 false
    /// </summary>
    bool TryReadDo(int index, out bool on);

    /// <summary>
    /// 写一个 DO
    /// </summary>
    bool WriteDo(int index, bool on);

    /// <summary>
    /// 读一个 AI 的原始码，读不到返回 false
    /// </summary>
    bool TryReadAi(int index, out double value);

    /// <summary>
    /// 回读一个 AO 的原始值，读不到返回 false
    /// </summary>
    bool TryReadAo(int index, out double value);

    /// <summary>
    /// 写一个 AO 的原始值
    /// </summary>
    bool WriteAo(int index, double value);
}
