namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// SubWafer1（@SubWafer1;）：订阅手指在位推送；订上以后手指有没有片一变，设备就主动推 SubWaferEx
/// </summary>
public sealed class RejeSubscribeWaferEventCommand : RejeCommand
{
    protected override string Wire => "SubWafer1";

    protected override bool Handle(RejeMessage message)
    {
        if (!string.Equals(message.Name, Wire, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (message.Code != RejeProtocol.SuccessCode)
        {
            Fail($"{message.Code}#{message.Content}");
            return true;
        }

        Complete();
        return true;
    }
}
