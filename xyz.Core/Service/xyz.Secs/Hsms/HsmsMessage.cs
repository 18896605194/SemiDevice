namespace xyz.Secs.Hsms;

/// <summary>
/// 收到的 HSMS 消息：完整头 + 数据项。发送用 SecsMessage，接收用这个（多了 DeviceId/PType/SType）。
/// 回对方 primary 时用 CreateReply 造 secondary，再交给 HsmsSession.Reply。
/// </summary>
public sealed class HsmsMessage
{
    public HsmsMessage(HsmsHeader header, SecsII.SecsItem? body)
    {
        Header = header;
        Body = body;
    }

    public HsmsHeader Header { get; }

    public SecsII.SecsItem? Body { get; }

    public bool IsDataMessage => Header.SType == 0;

    /// <summary>报文名：数据消息 "S1F13 W"，控制消息用类型名。</summary>
    public string Name => Header.SType == 0
        ? $"S{Header.Stream}F{Header.Function}{(Header.ReplyExpected ? " W" : string.Empty)}"
        : Header.MessageType.ToString();

    /// <summary>
    /// 按这条 primary 造 secondary：Function = primary + 1（E5 惯例：primary 奇数、secondary 偶数），
    /// SystemBytes 原样带回，W-Bit 强制 0。
    /// </summary>
    public SecsII.SecsMessage CreateReply(SecsII.SecsItem? body = null) =>
        new(Header.Stream, (byte)(Header.Function + 1), replyExpected: false, body) { SystemBytes = Header.SystemBytes };

    public override string ToString() => Name;
}
