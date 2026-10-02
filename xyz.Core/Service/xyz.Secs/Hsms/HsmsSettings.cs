namespace xyz.Secs.Hsms;

/// <summary>
/// HSMS 时序与连接参数。字段默认值取 SEMI E37 的常见出厂值，现场按 EAP 要求调。
/// </summary>
public sealed class HsmsSettings
{
    /// <summary>设备号（SessionId）：数据消息头里带上，双方要一致，不一致的报文会被丢弃记警告。</summary>
    public ushort DeviceId { get; set; }

    /// <summary>
    /// 本端是设备（不是 Host）：处理不了的报文回 S9（S9 只能设备发），T3 超时按 E37.1 §5 发 S9F9；
    /// Host 端（false）不发 S9，对方要回复的回同 Stream 的 F0 中止事务。跟 TCP 主动/被动模式无关。
    /// </summary>
    public bool IsEquipment { get; set; }

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

    /// <summary>本实现的资源限制（非 SEMI 协议上限）。大配方测试可调大。</summary>
    public int MaxFrameLength { get; set; } = 32 * 1024 * 1024;

    public int SendTimeoutMs { get; set; } = 10_000;

    /// <summary>检查参数，不合法抛 ArgumentException；Listener/Connector 的 Start 也会先调它。</summary>
    public void Validate()
    {
        if (DeviceId > 0x7FFF) throw new ArgumentOutOfRangeException(nameof(DeviceId), "HSMS-SS DeviceID 必须是 15 位");
        if (Port < 0 || Port > 65535) throw new ArgumentOutOfRangeException(nameof(Port));
        if (T3ReplyTimeoutMs <= 0 || T5ConnectRetryMs <= 0 || T6ControlTimeoutMs <= 0
            || T7NotSelectedTimeoutMs <= 0 || T8IntercharacterTimeoutMs <= 0
            || SendTimeoutMs <= 0 || LinktestIntervalMs < 0 || MaxFrameLength < 10)
            throw new ArgumentException("超时必须为正值，Linktest 可为 0，帧上限至少为 10");
    }
}
