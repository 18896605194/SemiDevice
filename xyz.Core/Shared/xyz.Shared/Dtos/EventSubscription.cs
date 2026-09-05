using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 事件订阅请求。当前后端全量下发、客户端按本地注册类型过滤；
/// Filters 为将来的 (TypeName, Token) 精确订阅预留。
/// </summary>
[ProtoContract]
public class EventSubscription
{
    /// <summary>
    /// 订阅过滤键，预留字段。
    /// </summary>
    [ProtoMember(1)]
    public List<string> Filters { get; set; } = new();
}
