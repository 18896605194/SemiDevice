using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface ILoadPortService
{
    [OperationContract]
    Task<RpcResponse> LoadAsync(string module);

    [OperationContract]
    Task<RpcResponse> UnloadAsync(string module);

    [OperationContract]
    Task<RpcResponse> HomeAsync(string module);

    [OperationContract]
    Task<RpcResponse> ResetAsync(string module);

    [OperationContract]
    Task<RpcResponse> AbortAsync(string module);

    [OperationContract]
    Task<RpcResponse> OnlineAsync(string module);

    [OperationContract]
    Task<RpcResponse> OfflineAsync(string module);

    [OperationContract]
    Task<RpcResponse> AutoAsync(string module);

    [OperationContract]
    Task<RpcResponse> ManualAsync(string module);

    [OperationContract]
    Task<RpcResponse> ReadCarrierIdAsync(string module);

    [OperationContract]
    Task<RpcResponse> GetStateAsync(string module);
}
