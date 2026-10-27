using xyz.Elements.Attributes;
using xyz.Drivers.Communication;

namespace xyz.Elements.Elements;

/// <summary>
/// 串口通道：设备经串口收发，参数只有串口的；同一台设备改接网口时，在 machine.xml 里启用旁边的网口通道、关掉这条就行
/// </summary>
[Element(description: "设备通道：串口（参数只有串口的；跟网口通道都配上时用 IsEnable 选一条）")]
public class SerialDriverElement : CommDriverElement
{
    #region Machine

    [Machine("COM", "Driver", "串口名称，如 COM2")]
    public string PortName { get; set; } = "COM";

    [Machine("9600", "Driver", "波特率")]
    public int BaudRate { get; set; } = 9600;

    [Machine("None", "Driver", "校验位：None / Odd / Even / Mark / Space")]
    public string Parity { get; set; } = "None";

    [Machine("8", "Driver", "数据位")]
    public int DataBits { get; set; } = 8;

    [Machine("One", "Driver", "停止位：One / OnePointFive / Two")]
    public string StopBits { get; set; } = "One";

    #endregion

    /// <summary>
    /// 字节传输：按上面的参数建串口
    /// </summary>
    protected override ICommunication CreateTransport()
    {
        return CommunicationFactory.CreateSerial(PortName, BaudRate, Parity, DataBits, StopBits);
    }
}
