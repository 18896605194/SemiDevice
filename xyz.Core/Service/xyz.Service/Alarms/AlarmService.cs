using ProtoBuf.Grpc;
using SqlSugar;
using xyz.Components.Components;
using xyz.Database.Alarms;
using xyz.Database.DbProvider;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Alarms;

/// <summary>
/// 报警 gRPC 服务：当前报警与人工复位走报警组件（AlarmComponent.Current），报警历史查报警记录表。
/// </summary>
public class AlarmService : IAlarmService
{
    public Task<RpcResponse> GetActiveAsync(RpcRequest request, CallContext context = default)
    {
        var alarms = AlarmComponent.Current?.ActiveAlarms.Select(item => item.ToDto()).ToList() ?? [];
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(alarms)));
    }

    /// <summary>
    /// 复位一个来源：走那个组件的 Reset（模块会顺带发设备复位），报警清掉后经事件流推给界面。
    /// </summary>
    public Task<RpcResponse> ResetAsync(RpcRequest request, CallContext context = default)
    {
        if (AlarmComponent.Current is not { } alarms)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.AlarmNotInstalled, []));
        }

        var source = request.Parameters.GetValueOrDefault("Source") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source) || !alarms.Reset(source))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.AlarmSourceNotFound, [source]));
        }

        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> ResetAllAsync(RpcRequest request, CallContext context = default)
    {
        if (AlarmComponent.Current is not { } alarms)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.AlarmNotInstalled, []));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(alarms.ResetAll())));
    }

    /// <summary>
    /// 查报警记录表（按天分表，只查时间段内已有的日表），再按等级、关键字筛，最新的在前。读库放线程池。
    /// </summary>
    public Task<RpcResponse> QueryHistoryAsync(AlarmHistoryQuery query, CallContext context = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var level = query.Level ?? string.Empty;
                var keyword = query.Keyword ?? string.Empty;
                var configured = AlarmComponent.Current?.HistoryQueryMaxCount ?? 0;
                if (configured <= 0)
                {
                    configured = 1000;
                }

                var maxCount = query.MaxCount > 0 ? Math.Min(query.MaxCount, configured) : configured;
                var start = query.Start;
                var end = query.End;

                using var db = XyzDb.Create(AlarmComponent.Current?.HistoryDatabase ?? XyzDb.DefaultName);
                var hasTables = db.SplitHelper<AlarmHistoryEntity>().GetTables()
                    .Any(table => table.Date >= start.Date && table.Date < end);
                if (!hasTables)
                {
                    return RpcResponse.Ok(JsonHelper.Serialize(new HistoryResult<AlarmHistoryDto>()));
                }

                var rows = db.Queryable<AlarmHistoryEntity>()
                    .SplitTable(start, end)
                    .Where(row => row.OccurredAt >= start && row.OccurredAt < end)
                    .WhereIF(level.Length > 0, row => row.Level == level)
                    .WhereIF(keyword.Length > 0,
                        row => row.Source.Contains(keyword) || row.Code.Contains(keyword) || row.Text.Contains(keyword))
                    .OrderBy(row => row.OccurredAt, OrderByType.Desc)
                    .Take(maxCount + 1)
                    .ToList();

                var result = new HistoryResult<AlarmHistoryDto>
                {
                    Items = rows.Take(maxCount).Select(row => row.ToDto()).ToList(),
                    Truncated = rows.Count > maxCount,
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
