using System.Text.Json;
using ProtoBuf;

namespace xyz.Tools;

/// <summary>
/// 事件信封。进程内发布订阅与跨进程 gRPC 传输共用的最小载体。
/// 路由键 = TypeName + Token，Payload 为消息体的 JSON。
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
    /// 是否留存：true = 状态类消息（每键保留最后一条，新订阅者立即补发）；
    /// false = 发生类消息（只给当时在线的订阅者，不留存）。
    /// </summary>
    [ProtoMember(5)]
    public bool Retain { get; set; }
}

/// <summary>
/// 信封打包/解包辅助，统一走 <see cref="JsonHelper"/>。
/// </summary>
public static class EventEnvelope
{
    /// <summary>
    /// 把消息对象打包成信封。retain 默认 false（上行/发生类），
    /// 后端 Send 状态类消息时显式传 true。
    /// </summary>
    public static EventMessage Of<TMessage>(TMessage message, string token = "", bool retain = false)
        where TMessage : class
    {
        return new EventMessage
        {
            TypeName = typeof(TMessage).FullName ?? string.Empty,
            Token = token ?? string.Empty,
            Payload = JsonHelper.Serialize(message),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Retain = retain,
        };
    }

    /// <summary>
    /// 把信封解包成指定类型的消息对象。
    /// </summary>
    public static object From(EventMessage envelope, Type type)
    {
        return JsonHelper.Deserialize(envelope.Payload, type)
               ?? throw new JsonException($"类型 {type.Name} 反序列化结果为 null");
    }
}
