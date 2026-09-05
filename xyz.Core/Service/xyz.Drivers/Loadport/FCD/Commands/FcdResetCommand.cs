namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Reset（SET:RESET）：复位清除错误。
/// </summary>
public sealed class FcdResetCommand : FcdCommand
{
    public FcdResetCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "RESET";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Set}:RESET";
    }
}
