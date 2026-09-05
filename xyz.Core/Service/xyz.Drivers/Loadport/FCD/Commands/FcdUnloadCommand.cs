namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Unload（MOV:CULOD）：关门。
/// </summary>
public sealed class FcdUnloadCommand : FcdCommand
{
    public FcdUnloadCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "CULOD";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:CULOD";
    }
}
