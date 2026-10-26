using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface IRobotService
{
    /// <summary>
    /// 回原点。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> HomeAsync(RpcRequest request);

    /// <summary>
    /// 复位。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ResetAsync(RpcRequest request);

    /// <summary>
    /// 急停，可顶替在途动作（被顶替动作的调用方收到 module.action_aborted）。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AbortAsync(RpcRequest request);

    /// <summary>
    /// 用指定手指从工位的槽位取片。参数：Module（string）、Arm（int，手指号）、Station（string，示教点表里的模块名）、Slot（int，层号）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PickAsync(RpcRequest request);

    /// <summary>
    /// 用指定手指向工位的槽位放片。参数同 <see cref="PickAsync"/>
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PlaceAsync(RpcRequest request);

    /// <summary>
    /// 伺服上使能。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PowerOnAsync(RpcRequest request);

    /// <summary>
    /// 伺服下使能。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PowerOffAsync(RpcRequest request);

    /// <summary>
    /// 切生产模式：交给调度用（只改 Mode，不经设备）。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetProductionAsync(RpcRequest request);

    /// <summary>
    /// 切维护模式：调度不再派新活（只改 Mode，不经设备）。参数：Module（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetMaintenanceAsync(RpcRequest request);

    /// <summary>
    /// 查询当前状态；Data 为 RobotDto（或其数组）的 JSON。参数：Module（string，可不带，空 = 全部 Robot）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(RpcRequest request);
}
