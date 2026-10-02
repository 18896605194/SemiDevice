namespace xyz.Secs.SecsII;

/// <summary>
/// 发送侧的 SECS-II 消息：Stream/Function/W-Bit/根数据项。
/// SystemBytes 由 HsmsSession 发送时统一分配，业务层不用管。
/// </summary>
public sealed class SecsMessage
{
    /// <param name="stream">Stream 号（如 S1 的 1）。</param>
    /// <param name="function">Function 号（如 F13 的 13）。</param>
    /// <param name="replyExpected">W-Bit：true 表示要求对端回复 secondary。</param>
    /// <param name="body">根数据项，S1F1 这类空报文传 null。</param>
    public SecsMessage(byte stream, byte function, bool replyExpected = false, SecsItem? body = null)
    {
        if (stream > 127) throw new ArgumentOutOfRangeException(nameof(stream), "Stream 必须在 0~127 之间");
        if (replyExpected && (function == 0 || function % 2 == 0 || stream == 9))
            throw new ArgumentException("仅非 S9 的奇数 Function primary 可以设置 W-Bit", nameof(replyExpected));
        Stream = stream;
        Function = function;
        ReplyExpected = replyExpected;
        Body = body;
    }

    public byte Stream { get; }

    public byte Function { get; }

    public bool ReplyExpected { get; }

    public SecsItem? Body { get; }

    /// <summary>事务号：发送时由会话分配，回 primary 的 secondary 必须带上同一个号。</summary>
    public uint SystemBytes { get; internal set; }

    /// <summary>报文名，日志用："S1F13 W" / "S1F14"。</summary>
    public string Name => $"S{Stream}F{Function}{(ReplyExpected ? " W" : string.Empty)}";

    public override string ToString() => Name;
}
