namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Map（MOV:MAPDO）：已经 Load 好（门开着）时原地扫一遍槽；扫完的槽图用 GET:MAPDT 取（<see cref="FcdGetMapCommand"/>）。
/// TDK 系协议：CLOAD 只装载、CLDMP 装载并扫（已 Load 状态下发不扫）、MAPDO 是 Load 状态下扫，所以单独扫片用 MAPDO。
/// </summary>
public sealed class FcdMapCommand : FcdCommand
{
    public FcdMapCommand(ILoadPortDriver driver) : base(driver)
    {
    }

    protected override string Name => "MAPDO";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:MAPDO";
    }
}
