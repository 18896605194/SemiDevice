using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 日志服务契约：客户端连上后拉取后端最近日志，补上「连接之前」发生的历史。
/// 实时日志仍走事件流（EventBus token = LogDto.EventToken）。
/// </summary>
[ServiceContract]
public interface ILogService
{
    /// <summary>
    /// 拉取最近若干条后端日志，按时间升序。Data 为 LogDto 数组的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetRecentAsync(LogQuery query, CallContext context = default);
}
