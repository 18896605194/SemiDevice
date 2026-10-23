namespace xyz._35021.Module.Drivers.Fcd.Rfid.Commands;

/// <summary>
/// 取读头状态：0x01 → 0x61
/// </summary>
public sealed class FcdGetStatusCommand : FcdRfidCommand
{
    /// <summary>
    /// 读头回的状态数据
    /// </summary>
    public byte[] Data { get; private set; } = [];

    protected override byte[] BuildBlock()
    {
        return [FcdRfidProtocol.CmdGetStatus];
    }

    protected override bool Handle(FcdRfidMessage message)
    {
        // 握手控制字符、校验不过的块链路层已经处理过，不是给指令的
        if (message.IsControl || !message.IsValid)
        {
            return false;
        }

        // 错误块能终结任何在途指令
        if (message.MessageId == FcdRfidProtocol.RspError)
        {
            Fail(FcdRfidProtocol.ErrorText(message.Data));
            return true;
        }

        if (message.MessageId != FcdRfidProtocol.RspStatus)
        {
            return false;
        }

        Data = message.Data;
        Complete();
        return true;
    }
}
