using xyz.Drivers.Communication.Serial;
using xyz.Drivers.Communication.Tcp;

namespace xyz.Drivers.Communication;

/// <summary>
/// 建传输：拿到的只是 ICommunication，串口/网口的具体类只在这里出现（串口通道、网口通道各调各的）。
/// </summary>
public static class CommunicationFactory
{
    /// <summary>
    /// 建串口
    /// </summary>
    public static ICommunication CreateSerial(string portName, int baudRate, string parity, int dataBits, string stopBits)
    {
        return new SerialCommunication().Create(portName, baudRate, parity, dataBits, stopBits);
    }

    /// <summary>
    /// 建网口（TCP 客户端）
    /// </summary>
    public static ICommunication CreateTcp(string host, int port)
    {
        return new TcpCommunication().Create(host, port);
    }
}
