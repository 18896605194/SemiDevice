using System.Text;

namespace xyz.Drivers.Rfid.FCD.Commands;

/// <summary>
/// 读标签取载具 ID：0xA0 → 0xE0。
/// 响应数据是 [起始页][页数][标签内存...]，载具 ID 按装机配的偏移与长度从标签内存里切。
/// </summary>
public sealed class FcdReadCarrierIdCommand : RfidCommand
{
    /// <summary>响应数据里标签内存前面的两字节页头（起始页、页数）。</summary>
    private const int TagOffset = 2;

    private static readonly byte[] Expected = [FcdRfidProtocol.RspTagData];

    private readonly int _idStart;
    private readonly int _idLength;

    /// <param name="idStart">载具 ID 在标签内存里的起始偏移。</param>
    /// <param name="idLength">载具 ID 字节数。</param>
    public FcdReadCarrierIdCommand(int idStart, int idLength)
    {
        _idStart = Math.Max(0, idStart);
        _idLength = Math.Max(1, idLength);
    }

    public override string Name => "ReadCarrierId";

    public override IReadOnlyList<byte> ExpectedResponseIds => Expected;

    public override byte[] BuildBlock()
    {
        return [FcdRfidProtocol.CmdReadTag, 0x00];
    }

    public override void ParseBlock(byte messageId, byte[] data)
    {
        if (messageId == FcdRfidProtocol.RspError)
        {
            Complete(RfidResponse.Fail(data.Length > 0 ? $"ERR{data[0]:D2}" : "ERR"));
            return;
        }

        if (messageId != FcdRfidProtocol.RspTagData)
        {
            return;
        }

        string carrierId = ExtractCarrierId(data);
        Complete(carrierId.Length > 0
            ? RfidResponse.Ok(data, carrierId)
            : RfidResponse.Fail("EmptyTag"));
    }

    /// <summary>
    /// 从标签内存里切出载具 ID：跳过两字节页头，按偏移取指定长度，去掉尾部填充。
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
