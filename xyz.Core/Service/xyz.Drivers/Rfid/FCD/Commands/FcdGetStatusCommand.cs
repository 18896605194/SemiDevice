namespace xyz.Drivers.Rfid.FCD.Commands;

/// <summary>
/// 取读头状态：0x01 → 0x61。
/// </summary>
public sealed class FcdGetStatusCommand : RfidCommand
{
    private static readonly byte[] Expected = [FcdRfidProtocol.RspStatus];

    public override string Name => "GetStatus";

    public override IReadOnlyList<byte> ExpectedResponseIds => Expected;

    public override byte[] BuildBlock()
    {
        return [FcdRfidProtocol.CmdGetStatus];
    }

    public override void ParseBlock(byte messageId, byte[] data)
    {
        if (messageId == FcdRfidProtocol.RspError)
        {
            Complete(RfidResponse.Fail(data.Length > 0 ? $"ERR{data[0]:D2}" : "ERR"));
            return;
        }

        if (messageId == FcdRfidProtocol.RspStatus)
        {
            Complete(RfidResponse.Ok(data));
        }
    }
}
