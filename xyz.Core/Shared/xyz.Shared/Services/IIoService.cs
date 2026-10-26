using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;


[ServiceContract]
public interface IIoService
{
    [OperationContract]
    Task<RpcResponse> WriteDoAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> WriteAoAsync(RpcRequest request);
}
