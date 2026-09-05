namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Abort（MOV:ABORT）：终止当前动作。
/// </summary>
public sealed class FcdAbortCommand : FcdCommand
{
    public FcdAbortCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "ABORT";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:ABORT";
    }
}
