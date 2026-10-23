namespace xyz.Drivers.Communication;

/// <summary>
/// 链路层握手：发之前要先问、收到要回执的协议，由品牌的解码器顺带实现，通道照它收发；没有握手的协议不用管
/// </summary>
public interface IFrameLink
{
    /// <summary>
    /// 一条指令要发了：返回现在就发出去的帧（比如先只发问询，数据等对方答应了再发）
    /// </summary>
    IReadOnlyList<string> Start(string frame);

    /// <summary>
    /// 收到一条报文：链路自己的（握手、回执）在这里处理掉，回要马上发给对方的帧，和这条报文还要不要交给指令
    /// </summary>
    LinkStep Receive(object message);

    /// <summary>
    /// 连接开了或关了：握手做到一半的作废
    /// </summary>
    void Reset();
}

/// <summary>
/// 链路层收到一条报文以后怎么办
/// </summary>
public sealed class LinkStep
{
    /// <summary>
    /// 不是链路的事：原样交给指令
    /// </summary>
    public static readonly LinkStep PassOn = new();

    /// <summary>
    /// 要马上回给对方的帧（握手应答、回执、轮到发的数据块）
    /// </summary>
    public IReadOnlyList<string> Replies { get; init; } = [];

    /// <summary>
    /// 链路自己处理完了，不再交给指令
    /// </summary>
    public bool Consumed { get; init; }

    /// <summary>
    /// 对方拒收在途指令的原因（比如回了 NAK），在途指令按它落失败；没拒收为 null
    /// </summary>
    public string? Rejected { get; init; }
}
