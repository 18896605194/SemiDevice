using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface IRobotService
{
    [OperationContract]
    Task<RpcResponse> HomeAsync(string module);

    [OperationContract]
    Task<RpcResponse> ResetAsync(string module);

    /// <summary>
    /// 急停，可顶替在途动作（被顶替动作的调用方收到 module.action_aborted）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> AbortAsync(string module);

    /// <summary>
    /// 用指定手指从工位的槽位取片。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PickAsync(RobotPickPlaceRequest request);

    /// <summary>
    /// 用指定手指向工位的槽位放片。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PlaceAsync(RobotPickPlaceRequest request);

    /// <summary>
    /// 伺服上使能。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PowerOnAsync(string module);

    /// <summary>
    /// 伺服下使能。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PowerOffAsync(string module);

    /// <summary>
    /// 查询当前状态。module 为空返回全部 Robot；Data 为 RobotDto（或其数组）的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(string module);
}
