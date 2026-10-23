namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// PowerOn（@PowerOn;）：伺服上使能，只在自动模式有效
/// </summary>
public sealed class RejePowerOnCommand : RejeCommand
{
    protected override string Wire => "PowerOn";

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
