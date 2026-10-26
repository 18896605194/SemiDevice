using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// LoadPort 服务契约：除了查状态，参数都是 Module（string，模块名）
/// </summary>
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
    /// 查询当前状态。参数：Module（string，可不带，空 = 全部 LoadPort）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(RpcRequest request);
}
