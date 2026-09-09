using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 日志查询请求。code-first gRPC 的请求参数必须是消息类（值类型不行），故包一层。
/// </summary>
[ProtoContract]
public class LogQuery
{
    /// <summary>
    /// 拉取条数；&lt;=0 表示按后端缓冲上限返回。
    /// </summary>
    [ProtoMember(1)]
    public int Count { get; set; } = 200;
}
