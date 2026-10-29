using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 供液操作服务
/// </summary>
[ServiceContract]
public interface ISupplyService
{
    /// <summary>
    /// 切换生产模式
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetProductionAsync(RpcRequest request);

    /// <summary>
    /// 切换维护模式
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetMaintenanceAsync(RpcRequest request);
}
