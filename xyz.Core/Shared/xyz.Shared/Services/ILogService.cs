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
    /// 最近一段日志。参数：Count（int，可不带，&lt;=0 = 后端缓冲里有多少给多少）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetRecentAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 按时间段查主日志。参数：Start（DateTime，含）、End（DateTime，不含）、Level（string，可不带）、Keyword（string，可不带）、
    /// MaxCount（int，可不带，&lt;=0 按后端配置）、Module（string，可不带，日志的第一层）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryHistoryAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> GetSettingsAsync(RpcRequest request, CallContext context = default);
}
