using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 通用 RPC 请求。
/// </summary>
[ProtoContract]
public class RpcRequest
{
    [ProtoMember(1)]
    public Dictionary<string, string> Parameters { get; set; } = new();
}
