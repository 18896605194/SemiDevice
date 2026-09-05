namespace xyz.Drivers.Communication;

/// <summary>
/// 串口网口的接口
/// </summary>

public interface ICommunication : IDisposable
{
    bool IsConnected { get; }

    void Connect();

    void Close();

    void Send(byte[] data);

    byte[] Receive();
}
