using xyz.Tools;
using ProtoBuf.Grpc;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Service.Events;

/// <summary>
/// 日志 gRPC 服务：客户端连上后拉取后端最近日志（历史），实时日志走事件流。
/// </summary>
public class LogService : ILogService
{
    public Task<RpcResponse> GetRecentAsync(LogQuery query, CallContext context = default)
    {
        var logs = LogHistory.Snapshot();
        var count = query.Count;
        if (count > 0 && logs.Count > count)
        {
            logs = logs.Skip(logs.Count - count).ToList();
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(logs)));
    }
}
