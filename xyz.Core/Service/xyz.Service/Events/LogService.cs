using xyz.Tools;
using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;

namespace xyz.Service.Events;

/// <summary>
/// 日志 gRPC 服务：客户端连上后拉取后端最近日志（历史），实时日志走事件流；日志历史页按时间段查日志文件。
/// </summary>
public class LogService : ILogService
{
    /// <summary>
    /// 历史查询默认最多返回条数。
    /// </summary>
    private const int DefaultMaxCount = 1000;

    /// <summary>
    /// 历史查询返回条数上限，免得一次把几十万行推给界面。
    /// </summary>
    private const int MaxCountLimit = 5000;

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
                var maxCount = query.MaxCount > 0 ? Math.Min(query.MaxCount, MaxCountLimit) : DefaultMaxCount;

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
}
