namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// Home（MOV:ORGSH）：整机回零
/// </summary>
public sealed class FcdHomeCommand : FcdCommand
{
    protected override string Name => "ORGSH";

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Move}:ORGSH";
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
                // 回零做完了
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
