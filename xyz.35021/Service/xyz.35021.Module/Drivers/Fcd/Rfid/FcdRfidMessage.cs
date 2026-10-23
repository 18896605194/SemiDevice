namespace xyz._35021.Module.Drivers.Fcd.Rfid;

/// <summary>
/// 读头的一帧（解码器解好的）：单字节握手控制字符，或者一整块（校验过了才有 MSG_ID 和数据）
/// </summary>
public sealed class FcdRfidMessage
{
    /// <summary>
    /// 是握手控制字符（ENQ / EOT / ACK / NAK）
    /// </summary>
    public bool IsControl { get; init; }

    /// <summary>
    /// 控制字符本身（IsControl 时有效）
    /// </summary>
    public byte Control { get; init; }

    /// <summary>
    /// 块的校验对得上（不是控制字符时有效；对不上要回 NAK，也不交给指令）
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// 块的消息 ID
    /// </summary>
    public byte MessageId { get; init; }

    /// <summary>
    /// 块里 MSG_ID 后面的数据
    /// </summary>
    public byte[] Data { get; init; } = [];
}
