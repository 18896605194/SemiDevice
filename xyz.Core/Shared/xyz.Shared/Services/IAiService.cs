using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;


[ServiceContract]
public interface IAiService
{

    [OperationContract]
    Task<RpcResponse> GetStatusAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> ExplainAlarmAsync(RpcRequest request, CallContext context = default);
}
