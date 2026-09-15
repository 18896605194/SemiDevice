namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Unclamp（MOV:PODOP）：松开 FOUP。
/// </summary>
public sealed class FcdUnclampCommand : FcdCommand
{
    public FcdUnclampCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "PODOP";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:PODOP";
    }
}
