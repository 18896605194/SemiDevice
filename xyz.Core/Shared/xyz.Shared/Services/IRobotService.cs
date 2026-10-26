using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface IRobotService
{
    /// <summary>
    /// 回原点
    /// </summary>
    [OperationContract]
    Task<RpcResponse> HomeAsync(RpcRequest request);

    /// <summary>
    /// 复位
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ResetAsync(RpcRequest request);

    /// <summary>
    /// 急停
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AbortAsync(RpcRequest request);

    /// <summary>
    /// 取片
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PickAsync(RpcRequest request);

    /// <summary>
    /// 放片
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PlaceAsync(RpcRequest request);

    /// <summary>
    /// 伺服上使能
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PowerOnAsync(RpcRequest request);

    /// <summary>
    /// 伺服下使能
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PowerOffAsync(RpcRequest request);

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

    /// <summary>
    /// 查询当前状态（RobotDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(RpcRequest request);
}
