namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// Map（MOV:MAPDO）：已经 Load 好（门开着）时原地扫一遍槽，扫完的槽图用 GET:MAPDT 取；TDK 系协议 CLOAD 只装载、CLDMP 装载并扫、MAPDO 是 Load 状态下扫
/// </summary>
public sealed class FcdMapCommand : FcdCommand
{
    protected override string Name => "MAPDO";

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Move}:MAPDO";
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
                // 扫完了；槽图不在这条回执里，另发 GET:MAPDT 取
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
