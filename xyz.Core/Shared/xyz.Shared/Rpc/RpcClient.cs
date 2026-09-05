using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Shared.Rpc;

/// <summary>
/// 通用 gRPC 客户端（code-first）。
/// </summary>
public class RpcClient
{
    private readonly IRpcService _service;

    public RpcClient(string address = "http://localhost:5000")
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var channel = GrpcChannel.ForAddress(address);
        _service = channel.CreateGrpcService<IRpcService>();
    }

    public Task<RpcResponse> InvokeAsync(RpcRequest request)
    {
        return _service.InvokeAsync(request);
    }
}
