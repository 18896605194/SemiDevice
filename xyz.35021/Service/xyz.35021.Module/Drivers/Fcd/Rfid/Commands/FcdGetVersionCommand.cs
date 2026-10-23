namespace xyz._35021.Module.Drivers.Fcd.Rfid.Commands;

/// <summary>
/// 取读头版本：0x00 → 0x60；开机自检用，确认读头在线、协议对得上
/// </summary>
public sealed class FcdGetVersionCommand : FcdRfidCommand
{
    /// <summary>
    /// 读头回的版本数据
    /// </summary>
    public byte[] Data { get; private set; } = [];

    protected override byte[] BuildBlock()
    {
        return [FcdRfidProtocol.CmdGetVersion];
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

        if (message.MessageId != FcdRfidProtocol.RspVersion)
        {
            return false;
        }

        Data = message.Data;
        Complete();
        return true;
    }
}
