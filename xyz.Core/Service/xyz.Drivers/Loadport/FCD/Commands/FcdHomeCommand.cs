namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Home（MOV:ORGSH）：整机回零。
/// </summary>
public sealed class FcdHomeCommand : FcdCommand
{
    public FcdHomeCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "ORGSH";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:ORGSH";
    }
}
