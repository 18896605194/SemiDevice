namespace xyz.Components.Interfaces;

/// <summary>
/// PLC 读写出口
/// </summary>
public interface IPlc
{
    bool IsConnected { get; }


    bool TryReadDi(int index, out bool on);

    bool TryReadDo(int index, out bool on);

    bool WriteDo(int index, bool on);

    bool TryReadAi(int index, out double value);

    /// <summary>
    bool TryReadAo(int index, out double value);

    bool WriteAo(int index, double value);

    /// <summary>
    /// 登记轴注册
    /// </summary>
    /// <param name="path"></param>
    void Register(string path);

    bool TryReadBlock(string path, out byte[] block);

    bool WriteBlock(string path, byte[] data);
}
