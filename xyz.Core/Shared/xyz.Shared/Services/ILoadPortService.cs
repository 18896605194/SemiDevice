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

    /// <summary>
    /// 自动模式（内部模式位，不经设备协议）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> OnlineAsync(string module);

    /// <summary>
    /// 手动模式（内部模式位，不经设备协议）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> OfflineAsync(string module);

    /// <summary>
    /// 查询当前状态。module 为空返回全部 LoadPort；Data 为 LoadPortDto（或其数组）的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(string module);
}
