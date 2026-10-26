using xyz.Tools;
using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

namespace xyz.Service.Events;

/// <summary>
/// 日志服务 
/// </summary>
public class LogService : ILogService
{
    private readonly ILogComponent? _log;

    public LogService()
    {
        _log = IocHelper.GetService<ILogComponent>();
    }

    /// <summary>
    /// 多少条
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task<RpcResponse> GetRecentAsync(RpcRequest request, CallContext context = default)
    {
        var logs = _log?.Recent() ?? [];
        var count = request.Get(RpcParams.Count, 0);
        if (count > 0 && logs.Count > count)
        {
            logs = logs.Skip(logs.Count - count).ToList();
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(logs)));
    }

   /// <summary>
   /// 查询的区间，根据日期
   /// </summary>
   /// <param name="request"></param>
   /// <param name="context"></param>
   /// <returns></returns>
    public Task<RpcResponse> QueryHistoryAsync(RpcRequest request, CallContext context = default)
    {
        if (!request.TryGet<DateTime>(RpcParams.Start, out var start)
            || !request.TryGet<DateTime>(RpcParams.End, out var end))
        {
            return InvalidRequest();
        }

        string module = request.Get(RpcParams.Module, string.Empty);
        string level = request.Get(RpcParams.Level, string.Empty);
        string keyword = request.Get(RpcParams.Keyword, string.Empty);
        int maxCount = request.Get(RpcParams.MaxCount, 0);
        return Task.Run(() =>
        {
            try
            {
                var configured = _log?.HistoryQueryMaxCount ?? 0;
                if (configured <= 0)
                {
                    configured = 1000;
                }

                var limit = maxCount > 0 ? Math.Min(maxCount, configured) : configured;

                var kept = new Queue<LogItem>();
                bool truncated = false;
                foreach (var item in LogFileReader.Read(start, end))
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
                    if (kept.Count > limit)
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
    /// 设置查询的基础条件，显示多少条，最多实时日志保留多少条
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
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

    /// <summary>
    /// 请求参数不对回包（rpc.request_invalid）
    /// </summary>
    private static Task<RpcResponse> InvalidRequest()
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.RequestInvalid, []));
    }
}
