namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// Reset（@Reset;）：清控制器报错
/// </summary>
public sealed class RejeResetCommand : RejeCommand
{
    protected override string Wire => "Reset";

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
