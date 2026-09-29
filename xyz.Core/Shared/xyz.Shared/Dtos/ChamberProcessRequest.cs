using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 腔体起工艺请求。code-first gRPC 的请求参数必须是消息类（值类型不行），故包一层。
/// </summary>
[ProtoContract]
public class ChamberProcessRequest
{
    /// <summary>
    /// 模块实例名，如 "Chamber1"。
    /// </summary>
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// 配方名。配方怎么传、传什么等工艺定下来再收窄，现在先按名字给。
    /// </summary>
    [ProtoMember(2)]
    public string Recipe { get; set; } = string.Empty;
}
