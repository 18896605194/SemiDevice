namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// 查询 FCD LoadPort 固件版本（GET:VERSN）。数据随 ACK 返回，ACK 即终结；
/// 版本号在 Response.Content，如 02-04-04-LP300SIM。
/// </summary>
public sealed class FcdGetVersionCommand : FcdCommand
{
    public FcdGetVersionCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "VERSN";

    protected override bool CompleteOnAck => true;

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Get}:VERSN";
    }
}
