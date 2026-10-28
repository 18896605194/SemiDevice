using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface ILoadPortService
{
    [OperationContract]
    Task<RpcResponse> LoadAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> UnloadAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> HomeAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> ResetAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> AbortAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> SetProductionAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> SetMaintenanceAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> AutoAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> ManualAsync(RpcRequest request);

    [OperationContract]
    Task<RpcResponse> ReadCarrierIdAsync(RpcRequest request);

    /// <summary>
    /// 查询当前状态
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(RpcRequest request);
}
