namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// PowerOff（@PowerOff;）：伺服下使能，只在自动模式有效
/// </summary>
public sealed class RejePowerOffCommand : RejeCommand
{
    protected override string Wire => "PowerOff";

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
