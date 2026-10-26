using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 日志服务
/// </summary>
[ServiceContract]
public interface ILogService
{
    [OperationContract]
    Task<RpcResponse> GetRecentAsync(LogQuery query, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> QueryHistoryAsync(LogHistoryQuery query, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> GetSettingsAsync(RpcRequest request, CallContext context = default);
}
