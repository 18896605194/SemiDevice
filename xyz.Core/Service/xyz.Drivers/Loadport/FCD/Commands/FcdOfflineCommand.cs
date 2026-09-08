namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// E84 下线（SET:E84EN/00）→ ACK + INF 终结。
/// </summary>
public sealed class FcdOfflineCommand : FcdCommand
{
    public FcdOfflineCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "E84EN";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Set}:E84EN/00";
    }
}
