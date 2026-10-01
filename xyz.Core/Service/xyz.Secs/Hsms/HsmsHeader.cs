using System.Buffers.Binary;

namespace xyz.Secs.Hsms;

/// <summary>
/// HSMS 消息头（10 字节）：SessionId(2) + Stream(1) + Function/W-Bit(1) + PType(1) + SType(1) + SystemBytes(4)，全大端。
/// W-Bit 在 Function 字节的最高位；SType 非 0 是控制消息，头后没有数据项。
/// 作为 struct 只做头的读写与解析，不持有报文本体。
/// </summary>
public readonly struct HsmsHeader
{
    public const int Size = 10;

    public ushort DeviceId { get; init; }

    public byte Stream { get; init; }

    /// <summary>Function 号（低 7 位有效，编码时 W-Bit 占最高位）。</summary>
    public byte Function { get; init; }

    public bool ReplyExpected { get; init; }

    /// <summary>编码格式：0 = SECS-II，其他值本端不支持、回 Reject。</summary>
    public byte PType { get; init; }

    /// <summary>0 = 数据消息；1~9 = 控制消息（见 HsmsMessageType）。</summary>
    public byte SType { get; init; }

    /// <summary>事务号：primary 的发起方分配，secondary 必须原样带回。</summary>
    public uint SystemBytes { get; init; }

    public HsmsMessageType MessageType => (HsmsMessageType)SType;

    /// <summary>构造数据消息头。</summary>
    public static HsmsHeader CreateData(ushort deviceId, byte stream, byte function, bool replyExpected, uint systemBytes) =>
        new() { DeviceId = deviceId, Stream = stream, Function = function, ReplyExpected = replyExpected, SystemBytes = systemBytes };

    /// <summary>构造控制消息头（Stream/Function 无意义，除 Select.rsp/Deselect.rsp 的结果码放 Function 位）。</summary>
    public static HsmsHeader CreateControl(HsmsMessageType type, uint systemBytes, byte result = 0) =>
        new() { SType = (byte)type, Function = result, SystemBytes = systemBytes };

    /// <summary>写入 10 字节目标区（大端）。</summary>
    public void Write(Span<byte> target)
    {
        BinaryPrimitives.WriteUInt16BigEndian(target, DeviceId);
        target[2] = Stream;
        target[3] = (byte)(Function | (ReplyExpected ? 0x80 : 0));
        target[4] = PType;
        target[5] = SType;
        BinaryPrimitives.WriteUInt32BigEndian(target[6..], SystemBytes);
    }

    public static HsmsHeader Parse(ReadOnlySpan<byte> source) => new()
    {
        DeviceId = BinaryPrimitives.ReadUInt16BigEndian(source),
        Stream = source[2],
        Function = (byte)(source[3] & 0x7F),
        ReplyExpected = (source[3] & 0x80) != 0,
        PType = source[4],
        SType = source[5],
        SystemBytes = BinaryPrimitives.ReadUInt32BigEndian(source[6..]),
    };
}
