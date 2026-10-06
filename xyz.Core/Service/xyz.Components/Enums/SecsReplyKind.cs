namespace xyz.Components.Enums;

/// <summary>
/// 一条 Host 报文怎么回（EAP 各标准组件处理完交给链路去回）。
/// </summary>
public enum SecsReplyKind
{
    /// <summary>回正常的 secondary（Function + 1），带处理结果。</summary>
    Normal,

    /// <summary>回 SxF0 中止事务：这会儿不收这条（离线时 Host 发来的大部分报文都这么回）。</summary>
    Abort,

    /// <summary>回 S9：报文本身不对（数据格式、取值）。</summary>
    Error,

    /// <summary>不回：对方没要回复，或者 E30 规定这时候不理（还没建立通讯时除了 S1F13 都不理）。</summary>
    None,
}
