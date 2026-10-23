namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// Unclamp（MOV:PODOP）：松开 FOUP
/// </summary>
public sealed class FcdUnclampCommand : FcdCommand
{
    protected override string Name => "PODOP";

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Move}:PODOP";
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
                // 松开了
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
