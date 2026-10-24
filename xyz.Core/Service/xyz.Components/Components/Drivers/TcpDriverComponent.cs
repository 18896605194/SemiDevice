using xyz.Components.Attributes;
using xyz.Drivers.Communication;

namespace xyz.Components.Components;

/// <summary>
/// 网口通道（TCP 客户端）：设备经网口收发，参数只有网口的；同一台设备改接串口时，在 machine.xml 里启用旁边的串口通道、关掉这条就行
/// </summary>
[Component(description: "设备通道：网口（TCP 客户端，参数只有网口的；跟串口通道都配上时用 IsEnable 选一条）")]
public class TcpDriverComponent : DeviceDriverComponent
{
    #region Machine

    [Machine("127.0.0.1", "Driver", "设备的 IP")]
    public string Host { get; set; } = "127.0.0.1";

    [Machine("0", "Driver", "设备的端口")]
    public int NetPort { get; set; }

    #endregion

    /// <summary>
    /// 字节传输：按上面的 IP、端口建网口
    /// </summary>
    protected override ICommunication CreateTransport()
    {
        return CommunicationFactory.CreateTcp(Host, NetPort);
    }
}
