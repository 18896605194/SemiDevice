namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// E84 上线（SET:E84EN/01）→ ACK + INF 终结。
/// </summary>
public sealed class FcdOnlineCommand : FcdCommand
{
    public FcdOnlineCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "E84EN";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Set}:E84EN/01";
    }
}
