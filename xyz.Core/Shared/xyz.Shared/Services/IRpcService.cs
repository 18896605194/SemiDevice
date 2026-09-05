using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;


[ServiceContract]
public interface IRpcService
{
    /// <summary>
    /// 通用调用入口。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> InvokeAsync(RpcRequest request, CallContext context = default);
}
