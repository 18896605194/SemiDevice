namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Load（MOV:CLOAD）：夹紧、对接、开锁、开门，只装载、不扫片；回执带不带槽图都不用，槽图另发 MAPDO 扫、GET:MAPDT 取。
/// </summary>
public sealed class FcdLoadCommand : FcdCommand
{
    public FcdLoadCommand(ILoadPortDriver driver) : base(driver)
    {
    }

    protected override string Name => "CLOAD";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:CLOAD";
    }
}
