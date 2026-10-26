using ProtoBuf.Grpc;
using SqlSugar;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Database.Alarms;
using xyz.Database.DbProvider;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Alarms;

/// <summary>
/// 报警服务
/// </summary>
public class AlarmService : IAlarmService
{
    private readonly IAlarm? _alarms;

    private readonly IMapper _mapper;

    public AlarmService()
    {
        _alarms = IocHelper.GetService<IAlarm>();
        _mapper = IocHelper.GetService<IMapper>() ?? throw new InvalidOperationException("machine.xml 没配映射组件（Mapper）");
    }

    /// <summary>
    /// 获取活跃的报警
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task<RpcResponse> GetActiveAsync(RpcRequest request, CallContext context = default)
    {
        var alarms = _alarms?.ActiveAlarms.Select(item => _mapper.Map<AlarmDto>(item)).ToList() ?? [];
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(alarms)));
    }

    /// <summary>
    /// Reset
    /// </summary>
    public Task<RpcResponse> ResetAsync(RpcRequest request, CallContext context = default)
    {
        if (_alarms is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.AlarmNotInstalled, []));
        }

        var source = request.Parameters.GetValueOrDefault("Source") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source) || !_alarms.Reset(source))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.AlarmSourceNotFound, [source]));
        }

        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// ResetAllAsync
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task<RpcResponse> ResetAllAsync(RpcRequest request, CallContext context = default)
    {
        if (_alarms is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.AlarmNotInstalled, []));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(_alarms.ResetAll())));
    }

    /// <summary>
    /// 查报警记录表
    /// </summary>
    public Task<RpcResponse> QueryHistoryAsync(AlarmHistoryQuery query, CallContext context = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var level = query.Level ?? string.Empty;
                var keyword = query.Keyword ?? string.Empty;
                var configured = _alarms?.HistoryQueryMaxCount ?? 0;
                if (configured <= 0)
                {
                    configured = 1000;
                }

                var maxCount = query.MaxCount > 0 ? Math.Min(query.MaxCount, configured) : configured;
                var start = query.Start;
                var end = query.End;

                using var db = XyzDb.Create(_alarms?.HistoryDatabase ?? XyzDb.DefaultName);
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
                    Items = rows.Take(maxCount).Select(row => _mapper.Map<AlarmHistoryDto>(row)).ToList(),
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
