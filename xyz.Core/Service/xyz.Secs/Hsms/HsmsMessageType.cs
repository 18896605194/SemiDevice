namespace xyz.Secs.Hsms;

/// <summary>
/// HSMS 消息类型：SType 字段的含义。Data 之外都是控制消息（10 字节头、无数据项）。
/// </summary>
public enum HsmsMessageType : byte
{
    /// <summary>SECS-II 数据消息（SType=0，PType=0，头后跟 Item 树）。</summary>
    Data = 0,

    SelectReq = 1,
    SelectRsp = 2,
    DeselectReq = 3,
    DeselectRsp = 4,
    LinktestReq = 5,
    LinktestRsp = 6,

    /// <summary>拒绝：对方发了本端不能处理的消息（头 byte3 带原因码）。</summary>
    RejectReq = 7,

    /// <summary>单向下行：请求断开，不需要回复。</summary>
    SeparateReq = 9,
}
