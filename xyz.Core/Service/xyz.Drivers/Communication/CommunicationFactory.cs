using xyz.Drivers.Communication.Serial;
using xyz.Drivers.Communication.Tcp;

namespace xyz.Drivers.Communication;

/// <summary>
/// 建传输：拿到的只是 ICommunication，串口/网口的具体类只在这里出现。
/// </summary>
public static class CommunicationFactory
{
    /// <summary>
    /// 按 sc.xml 配的通讯类型建（LoadPort、RFID 这种串口网口都可能的设备用）。
    /// </summary>
    public static ICommunication Create(CommType commType, string portName, int baudRate, string parity,
        int dataBits, string stopBits, string host, int netPort)
    {
        return commType switch
        {
            CommType.Serial => CreateSerial(portName, baudRate, parity, dataBits, stopBits),
            CommType.Tcp => CreateTcp(host, netPort),
            _ => throw new NotSupportedException($"不支持的通讯类型: {commType}"),
        };
    }

    public static ICommunication CreateSerial(string portName, int baudRate, string parity, int dataBits, string stopBits)
    {
        return new SerialCommunication().Create(portName, baudRate, parity, dataBits, stopBits);
    }

    public static ICommunication CreateTcp(string host, int port)
    {
        return new TcpCommunication().Create(host, port);
    }
}
