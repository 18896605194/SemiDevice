using ProtoBuf;

namespace xyz.Tools;

/// <summary>
/// 跨进程事件信封：RemoteEventBus 打包、拆包用，业务代码不碰
/// </summary>
[ProtoContract]
public class EventMessage
{
    /// <summary>
    /// 消息类型全名（路由键之一）。
    /// </summary>
    [ProtoMember(1)]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// 字符串 token，区分同类型消息的不同实例（如 "LoadPort1"/"LoadPort2"）。空串表示全局消息。
    /// </summary>
    [ProtoMember(2)]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 消息体 JSON。
    /// </summary>
    [ProtoMember(3)]
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// 发布时刻的时间戳（Unix 毫秒）。
    /// </summary>
    [ProtoMember(4)]
    public long Timestamp { get; set; }

    /// <summary>
    /// 要不要写黑板：true 是状态类（每格只留最后一条，后订阅的马上补发），false 是通知类（只给当时连着的，同一格擦掉）
    /// </summary>
    [ProtoMember(5)]
    public bool Retain { get; set; }
}
