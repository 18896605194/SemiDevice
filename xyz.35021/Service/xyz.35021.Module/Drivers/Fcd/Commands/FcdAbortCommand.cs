namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// Abort（MOV:ABORT）：终止当前动作
/// </summary>
public sealed class FcdAbortCommand : FcdCommand
{
    protected override string Name => "ABORT";

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Move}:ABORT";
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
                // 停下来了
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
