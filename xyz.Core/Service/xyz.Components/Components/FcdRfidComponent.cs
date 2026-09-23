using xyz.Components.Attributes;
using xyz.Drivers.Communication;
using xyz.Drivers.Rfid;
using xyz.Drivers.Rfid.FCD;
using xyz.Drivers.Rfid.FCD.Commands;

namespace xyz.Components.Components;

[Component(description: "富创得 RFID 读头驱动组件（RFT-200S）")]
public class FcdRfidComponent : RfidDriverComponent
{
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
}
