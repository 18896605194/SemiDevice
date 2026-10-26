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
    /// <summary>
    /// 获取最近一段日志
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetRecentAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 按时间段查询主日志
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryHistoryAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> GetSettingsAsync(RpcRequest request, CallContext context = default);
}
