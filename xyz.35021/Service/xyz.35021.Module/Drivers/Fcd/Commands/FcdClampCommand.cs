namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// Clamp（MOV:PODCL）：夹紧 FOUP
/// </summary>
public sealed class FcdClampCommand : FcdCommand
{
    protected override string Name => "PODCL";

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Move}:PODCL";
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
                // 夹紧了
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
