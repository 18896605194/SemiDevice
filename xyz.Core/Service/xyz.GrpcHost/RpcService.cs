using ProtoBuf.Grpc;
using System.Text.Json;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.GrpcHost;

/// <summary>
/// 通用 RPC 服务实现（code-first）。
/// 目前保留为通用回显，后续可按需接入其他服务。
/// </summary>
public class RpcService : IRpcService
{
    public Task<RpcResponse> InvokeAsync(RpcRequest request, CallContext context = default)
    {
        var response = RpcResponse.Ok(JsonSerializer.Serialize(request.Parameters));
        return Task.FromResult(response);
    }
}
