using xyz.Components.Attributes;
using xyz.Drivers.Communication;
using xyz.Drivers.Rfid;
using xyz.Drivers.Rfid.FCD;
using xyz.Drivers.Rfid.FCD.Commands;
using xyz.Modules;

namespace xyz._35021.Module.Rfid;

/// <summary>
/// 35021 机台 RFID 读头：FCD（富创得 RFT-200S）驱动。
/// 挂在 sc.xml 的 LoadPort 节点下面，名字固定叫 RFID。
/// </summary>
[Component(description: "35021 RFID 读头组件")]
public class RfidReaderComponent : BaseRfidReader
{
    #region 驱动连接

    protected override IRfidDriver CreateDriver()
    {
        // 二进制协议：帧通讯用 Latin1，字节过 string 管道无损。
        return new FcdRfidDriver(
            new FrameCommunication(CreateTransport(), new FcdRfidFrameCodec(), FcdRfidProtocol.Binary));
    }

    protected override RfidCommand CreateReadCarrierIdCommand()
    {
        return new FcdReadCarrierIdCommand(IdStart, IdLength);
    }

    #endregion
}
