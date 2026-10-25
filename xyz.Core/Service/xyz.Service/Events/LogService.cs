using xyz.Tools;
using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;

namespace xyz.Service.Events;

/// <summary>
/// 日志 gRPC 服务：客户端连上后拉取后端最近日志（历史），实时日志走事件流；日志历史页按时间段查日志文件；
/// 客户端日志显示条数也从这里取（machine.xml 的 Log 节点）。
/// </summary>
public class LogService : ILogService
{
    private readonly ILogComponent? _log;

    public LogService()
    {
        _log = IocHelper.GetService<ILogComponent>();
    }

    public Task<RpcResponse> GetRecentAsync(LogQuery query, CallContext context = default)
    {
        var logs = _log?.Recent() ?? [];
        var count = query.Count;
        if (count > 0 && logs.Count > count)
        {
            logs = logs.Skip(logs.Count - count).ToList();
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(logs)));
    }

    /// <summary>
    /// 按时间段读主日志文件，再按模块（第一层完全一样）、级别、关键字（模块、部件、内容里包含）筛；超过条数只留最新的。读文件放线程池，不占 gRPC 线程。
    /// </summary>
    public Task<RpcResponse> QueryHistoryAsync(LogHistoryQuery query, CallContext context = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var module = query.Module ?? string.Empty;
                var level = query.Level ?? string.Empty;
                var keyword = query.Keyword ?? string.Empty;
                var configured = _log?.HistoryQueryMaxCount ?? 0;
                if (configured <= 0)
                {
                    configured = 1000;
                }

                var maxCount = query.MaxCount > 0 ? Math.Min(query.MaxCount, configured) : configured;

                var kept = new Queue<LogItem>();
                bool truncated = false;
                foreach (var item in LogFileReader.Read(query.Start, query.End))
                {
                    if (module.Length > 0 && !string.Equals(item.Module, module, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 收发原文没有级别（那一格写的是发 / 收），选了级别就不要它们
                    if (level.Length > 0
                        && (item.Direction.Length > 0 || !string.Equals(item.Level.Name, level, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (keyword.Length > 0
                        && !item.Module.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        && !item.Part.Contains(keyword, StringComparison.OrdinalIgnoreCase)
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
                            Level = LevelOf(item),
                            Module = item.Module,
                            Part = item.Part,
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
    /// 客户端的日志设置：显示条数、文件留几天、日志历史页的模块下拉选项，读 machine.xml 的 Log 节点（ILogComponent），没装或配得不对时给默认值。
    /// </summary>
    public Task<RpcResponse> GetSettingsAsync(RpcRequest request, CallContext context = default)
    {
        var settings = new LogSettingsDto
        {
            RealtimeDisplayMaxCount = PositiveOr(_log?.RealtimeDisplayMaxCount, LogSettingsDto.DefaultRealtimeDisplayMaxCount),
            LogBarDisplayMaxCount = PositiveOr(_log?.LogBarDisplayMaxCount, LogSettingsDto.DefaultLogBarDisplayMaxCount),
        };

        var log = _log;
        if (log is not null)
        {
            settings.KeepDays = log.KeepDays;
            settings.Modules = [.. log.Modules];
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(settings)));
    }

    /// <summary>
    /// 级别那一格：收发原文显示方向（发 / 收），别的显示级别
    /// </summary>
    private static string LevelOf(LogItem item)
    {
        if (item.Direction.Length > 0)
        {
            return item.Direction;
        }

        return item.Level.Name;
    }

    private static int PositiveOr(int? value, int fallback)
    {
        return value > 0 ? value.Value : fallback;
    }
}
