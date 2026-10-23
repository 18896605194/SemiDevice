using System.Text;

namespace xyz._35021.Module.Drivers.Fcd.Rfid.Commands;

/// <summary>
/// 读标签取载具 ID：0xA0 → 0xE0。响应数据是 [起始页][页数][标签内存...]，载具 ID 按装机配的偏移和长度从标签内存里切
/// </summary>
public sealed class FcdReadCarrierIdCommand : FcdRfidCommand
{
    /// <summary>
    /// 响应数据里标签内存前面的两字节页头（起始页、页数）
    /// </summary>
    private const int TagOffset = 2;

    private readonly int _idStart;
    private readonly int _idLength;

    /// <param name="idStart">载具 ID 在标签内存里的起始偏移。</param>
    /// <param name="idLength">载具 ID 字节数。</param>
    public FcdReadCarrierIdCommand(int idStart, int idLength)
    {
        _idStart = Math.Max(0, idStart);
        _idLength = Math.Max(1, idLength);
    }

    /// <summary>
    /// 读到的载具 ID
    /// </summary>
    public string CarrierId { get; private set; } = string.Empty;

    protected override byte[] BuildBlock()
    {
        return [FcdRfidProtocol.CmdReadTag, 0x00];
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

        if (message.MessageId != FcdRfidProtocol.RspTagData)
        {
            return false;
        }

        string carrierId = ExtractCarrierId(message.Data);
        if (carrierId.Length == 0)
        {
            Fail("EmptyTag");
            return true;
        }

        CarrierId = carrierId;
        Complete();
        return true;
    }

    /// <summary>
    /// 从标签内存里切出载具 ID：跳过两字节页头，按偏移取指定长度，去掉尾部填充
    /// </summary>
    private string ExtractCarrierId(byte[] data)
    {
        if (data.Length <= TagOffset)
        {
            return string.Empty;
        }

        int tagLength = data.Length - TagOffset;
        if (_idStart >= tagLength)
        {
            return string.Empty;
        }

        int count = Math.Min(_idLength, tagLength - _idStart);
        var text = new StringBuilder(count);
        for (int index = 0; index < count; index++)
        {
            text.Append((char)data[TagOffset + _idStart + index]);
        }

        return text.ToString().TrimEnd(' ', '\0');
    }
}
