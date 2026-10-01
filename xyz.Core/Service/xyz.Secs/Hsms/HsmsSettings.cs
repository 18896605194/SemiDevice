namespace xyz.Secs.Hsms;

/// <summary>
/// HSMS 时序与连接参数。字段默认值取 SEMI E37 的常见出厂值，现场按 EAP 要求调。
/// </summary>
public sealed class HsmsSettings
{
    /// <summary>设备号（SessionId）：数据消息头里带上，双方要一致，不一致的报文会被丢弃记警告。</summary>
    public ushort DeviceId { get; set; }

    /// <summary>true = 主动连出（连 EAP）；false = 被动监听（等 EAP 连入，设备端常规模式）。</summary>
    public bool IsActive { get; set; }

    /// <summary>对端地址（IsActive 时用）。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>HSMS 惯例端口 5000；注意与站内其他服务（本框架 gRPC 用 localhost:5000）的端口规划冲突。</summary>
    public int Port { get; set; } = 5000;

    /// <summary>T3：数据事务等回复的超时（发 S1F13 后最多等这么久）。</summary>
    public int T3ReplyTimeoutMs { get; set; } = 45_000;

    /// <summary>T5：断线后重连的等待间隔。</summary>
    public int T5ConnectRetryMs { get; set; } = 10_000;

    /// <summary>T6：控制事务（Select/Deselect/Linktest）等回复的超时。</summary>
    public int T6ControlTimeoutMs { get; set; } = 5_000;

    /// <summary>T7：Passive 方 TCP 连上后等多久没收到 Select.req 就断开。</summary>
    public int T7NotSelectedTimeoutMs { get; set; } = 10_000;

    /// <summary>T8：一帧内部字节间的最大间隔（读到一半断流判死）。帧间空闲不受它管，靠 Linktest 探活。</summary>
    public int T8IntercharacterTimeoutMs { get; set; } = 5_000;

    /// <summary>Linktest 心跳周期；SELECTED 后周期发 Linktest.req，T6 内没回就判链路死。0 = 不主动发（仍会应答对方）。</summary>
    public int LinktestIntervalMs { get; set; } = 30_000;
}
