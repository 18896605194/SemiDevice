namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// Load（MOV:CLOAD）：夹紧、对接、开锁、开门，只装载、不扫片；回执带不带槽图都不用，槽图另发 MAPDO 扫、GET:MAPDT 取
/// </summary>
public sealed class FcdLoadCommand : FcdCommand
{
    protected override string Name => "CLOAD";

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Move}:CLOAD";
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
                // 门开好了；回执里带的槽图不用
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
