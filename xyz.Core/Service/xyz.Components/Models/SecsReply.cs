using xyz.Components.Enums;
using xyz.Secs.SecsII;

namespace xyz.Components.Models;

/// <summary>
/// 一条 Host 报文的处理结果，由链路（HsmsComponent）照着回：正常回 Function + 1 带体、回 SxF0、回 S9，或者不回。
/// 处理方只管算结果，不碰会话——回复的发送、对方没要回复（W=0）时不发，都由链路统一做。
/// </summary>
public sealed class SecsReply
{
    /// <summary>S9F7：数据不对（类型、取值、结构）。</summary>
    public const byte IllegalDataFunction = 7;

    private SecsReply(SecsReplyKind kind, SecsItem? body, byte errorFunction)
    {
        Kind = kind;
        Body = body;
        ErrorFunction = errorFunction;
    }

    public SecsReplyKind Kind { get; }

    /// <summary>正常回复的体；null 表示只有头（E5 里有几条 secondary 就是空的）。</summary>
    public SecsItem? Body { get; }

    /// <summary>回 S9 时的 Function（3 不认识的 Stream、5 不认识的 Function、7 数据不对）。</summary>
    public byte ErrorFunction { get; }

    /// <summary>
    /// 回复发出去以后接着做的事（在派发线程上、处理下一条报文之前做）：比如回完 S1F18 再转在线、报在线事件，
    /// Host 先看到回复、后看到事件。
    /// </summary>
    public Action? AfterReply { get; private init; }

    /// <summary>回 SxF0 中止事务。</summary>
    public static SecsReply Abort { get; } = new(SecsReplyKind.Abort, null, 0);

    /// <summary>不回。</summary>
    public static SecsReply None { get; } = new(SecsReplyKind.None, null, 0);

    /// <summary>回 S9F7：报文里的数据不对。</summary>
    public static SecsReply IllegalData { get; } = new(SecsReplyKind.Error, null, IllegalDataFunction);

    /// <summary>正常回复（Function + 1）。</summary>
    public static SecsReply Of(SecsItem? body)
    {
        return new SecsReply(SecsReplyKind.Normal, body, 0);
    }

    /// <summary>回 S9Fx。</summary>
    public static SecsReply Error(byte function)
    {
        return new SecsReply(SecsReplyKind.Error, null, function);
    }

    /// <summary>同样的回复，回完再做 action。</summary>
    public SecsReply Then(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new SecsReply(Kind, Body, ErrorFunction) { AfterReply = action };
    }
}
