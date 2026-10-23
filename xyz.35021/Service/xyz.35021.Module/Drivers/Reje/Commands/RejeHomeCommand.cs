namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// Home（@Home;）：全轴回原点
/// </summary>
public sealed class RejeHomeCommand : RejeCommand
{
    protected override string Wire => "Home";

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
