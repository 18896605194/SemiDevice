namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// Unload（MOV:CULOD）：关门
/// </summary>
public sealed class FcdUnloadCommand : FcdCommand
{
    protected override string Name => "CULOD";

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Move}:CULOD";
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
                // 门关好了
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
