using xyz.Tools;
using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;

namespace xyz.Service.Events;

/// <summary>
/// 日志 gRPC 服务：客户端连上后拉取后端最近日志（历史），实时日志走事件流；日志历史页按时间段查日志文件；
/// 客户端日志显示条数也从这里取（sc.xml 的 Log 节点）。
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

    /// <summary>
    /// 按时间段读日志文件，再按级别、关键字筛；超过条数只留最新的。读文件放线程池，不占 gRPC 线程。
    /// </summary>
    public Task<RpcResponse> QueryHistoryAsync(LogHistoryQuery query, CallContext context = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var level = query.Level ?? string.Empty;
                var keyword = query.Keyword ?? string.Empty;
                var configured = LogComponent.Current?.HistoryQueryMaxCount ?? LogComponent.DefaultHistoryQueryMaxCount;
                if (configured <= 0)
                {
                    configured = LogComponent.DefaultHistoryQueryMaxCount;
                }

                var maxCount = query.MaxCount > 0 ? Math.Min(query.MaxCount, configured) : configured;

                var kept = new Queue<LogItem>();
                bool truncated = false;
                foreach (var item in LogFileReader.Read(query.Start, query.End))
                {
                    if (level.Length > 0 && !string.Equals(item.Level.Name, level, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (keyword.Length > 0
                        && !item.Module.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        && !item.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    kept.Enqueue(item);
                    if (kept.Count > maxCount)
                    {
                        kept.Dequeue();
                        truncated = true;
                    }
                }

                var result = new HistoryResult<LogDto>
                {
                    Items = kept
                        .OrderByDescending(item => item.Time)
                        .Select(item => new LogDto
                        {
                            Time = item.Time,
                            Level = item.Level.Name,
                            Module = item.Module,
                            Message = item.Message,
                            Source = "Server",
                        })
                        .ToList(),
                    Truncated = truncated,
                };
                return RpcResponse.Ok(JsonHelper.Serialize(result));
            }
            catch (Exception exception)
            {
                return RpcResponse.Fail(ErrorCodes.HistoryQueryFailed, [exception.Message]);
            }
        });
    }

    /// <summary>
    /// 客户端日志显示条数：读 sc.xml 的 Log 节点（LogComponent.Current），没装或配得不对时给默认值。
    /// </summary>
    public Task<RpcResponse> GetSettingsAsync(RpcRequest request, CallContext context = default)
    {
        var component = LogComponent.Current;
        var settings = new LogSettingsDto
        {
            RealtimeDisplayMaxCount = PositiveOr(component?.RealtimeDisplayMaxCount, LogComponent.DefaultRealtimeDisplayMaxCount),
            LogBarDisplayMaxCount = PositiveOr(component?.LogBarDisplayMaxCount, LogComponent.DefaultLogBarDisplayMaxCount),
        };

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(settings)));
    }

    private static int PositiveOr(int? value, int fallback)
    {
        return value > 0 ? value.Value : fallback;
    }
}
