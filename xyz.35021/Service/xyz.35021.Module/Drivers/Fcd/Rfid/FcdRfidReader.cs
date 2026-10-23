using xyz._35021.Module.Drivers.Fcd.Rfid.Commands;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Drivers.Communication;

namespace xyz._35021.Module.Drivers.Fcd.Rfid;

/// <summary>
/// 富创得 RFID 读头（RFT-200S）：读码流程、超时、通道都是平台的，这里只给解码器和读码指令
/// </summary>
[Component(description: "富创得 RFID 读头（RFT-200S），下面挂一个 Driver 通道")]
public class FcdRfidReader : RfidReaderComponent<FcdReadCarrierIdCommand>
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
