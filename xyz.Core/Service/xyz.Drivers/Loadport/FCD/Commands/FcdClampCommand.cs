namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Clamp（MOV:PODCL）：夹紧 FOUP。
/// </summary>
public sealed class FcdClampCommand : FcdCommand
{
    public FcdClampCommand(ILoadPortDriver driver) : base(driver)
    {
    }

    protected override string Name => "PODCL";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:PODCL";
    }
}
