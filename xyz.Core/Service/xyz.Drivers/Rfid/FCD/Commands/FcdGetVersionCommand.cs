namespace xyz.Drivers.Rfid.FCD.Commands;

/// <summary>
/// 取读头版本：0x00 → 0x60。开机自检用，确认读头在线且协议对得上。
/// </summary>
public sealed class FcdGetVersionCommand : RfidCommand
{
    private static readonly byte[] Expected = [FcdRfidProtocol.RspVersion];

    public override string Name => "GetVersion";

    public override IReadOnlyList<byte> ExpectedResponseIds => Expected;

    public override byte[] BuildBlock()
    {
        return [FcdRfidProtocol.CmdGetVersion];
    }

    public override void ParseBlock(byte messageId, byte[] data)
    {
        if (messageId == FcdRfidProtocol.RspError)
        {
            Complete(RfidResponse.Fail(data.Length > 0 ? $"ERR{data[0]:D2}" : "ERR"));
            return;
        }

        if (messageId == FcdRfidProtocol.RspVersion)
        {
            Complete(RfidResponse.Ok(data));
        }
    }
}
