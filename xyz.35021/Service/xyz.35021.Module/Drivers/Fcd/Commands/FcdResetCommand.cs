namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// Reset（SET:RESET）：复位清错
/// </summary>
public sealed class FcdResetCommand : FcdCommand
{
    protected override string Name => "RESET";

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Set}:RESET";
    }

    protected override bool Handle(FcdMessage message)
    {
        if (!string.Equals(message.Name, Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        switch (message.Type)
        {
            case FcdProtocol.Ack:
                // 只是受理了，等 INF
                return true;

            case FcdProtocol.Inf:
                // 复位做完了
                Complete();
                return true;

            case FcdProtocol.Abs:
            case FcdProtocol.Nak:
                Fail($"{message.Type}:{Name}/{message.Data}");
                return true;

            default:
                return false;
        }
    }
}
