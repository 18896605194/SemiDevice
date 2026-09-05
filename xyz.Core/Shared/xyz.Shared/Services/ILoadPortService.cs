using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// LoadPort 手动操作 gRPC 服务契约（命令通道）。
/// 每个动作一个方法，与 xyz.Modules 的 ILoadPort 一一对应；
/// 返回 Success 表示设备已确认动作完成。
/// module 为模块实例名，与 EventBus token 一致，如 "LoadPort1"。
/// </summary>
[ServiceContract]
public interface ILoadPortService
{
    [OperationContract]
    Task<RpcResponse> LoadAsync(string module, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> UnloadAsync(string module, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> HomeAsync(string module, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> ResetAsync(string module, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> AbortAsync(string module, CallContext context = default);

    /// <summary>
    /// 查询当前状态。module 为空返回全部 LoadPort；Data 为 LoadPortDto（或其数组）的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStateAsync(string module, CallContext context = default);
}
