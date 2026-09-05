namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// 查询 FCD LoadPort 固件版本（GET:VERSN）。数据随 ACK 返回，ACK 即终结。
/// </summary>
public sealed class FcdGetVersionCommand : FcdCommand
{
    public FcdGetVersionCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "VERSN";

    /// <summary>
    /// 读到的版本号，如 02-04-04-LP300SIM。
    /// </summary>
    public string Version { get; private set; } = string.Empty;

    protected override bool CompleteOnAck => true;

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Get}:VERSN";
    }

    protected override void OnAck(string data)
    {
        Version = data;
    }
}
