using xyz._35021.Module.Drivers.Fcd.Rfid.Commands;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Drivers.Communication;

namespace xyz._35021.Module.Drivers.Fcd.Rfid;

/// <summary>
/// 富创得 RFID 读头（RFT-200S）：读码流程、超时、通道都是平台的，这里只给解码器和读码指令
/// </summary>
[Element(description: "富创得 RFID 读头（RFT-200S），下面挂一个 Driver 通道")]
public class FcdRfidReader : RfidReaderElement<FcdReadCarrierIdCommand>
{
    protected override IFrameCodec CreateCodec()
    {
        return new FcdRfidCodec();
    }

    protected override FcdReadCarrierIdCommand CreateReadCommand()
    {
        return new FcdReadCarrierIdCommand(IdStart, IdLength);
    }

    protected override string CarrierIdOf(FcdReadCarrierIdCommand command)
    {
        return command.CarrierId;
    }
}
