using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;


[ServiceContract]
public interface IRpcService
{
    [OperationContract]
    Task<RpcResponse> InvokeAsync(RpcRequest request, CallContext context = default);
}
