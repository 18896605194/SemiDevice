using ProtoBuf.Grpc;
using SqlSugar;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Database.Alarms;
using xyz.Database.DbProvider;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Alarms;

/// <summary>
/// 报警服务
/// </summary>
public class AlarmService : BaseService, IAlarmService
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
            return NotInstalled(ErrorCodes.AlarmNotInstalled);
        }

        if (!request.TryGet<string>(RpcParams.Source, out var source))
        {
            return InvalidRequest();
        }

        if (string.IsNullOrWhiteSpace(source) || !_alarms.Reset(source))
        {
            return Fail(ErrorCodes.AlarmSourceNotFound, source);
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
            return NotInstalled(ErrorCodes.AlarmNotInstalled);
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(_alarms.ResetAll())));
    }

    /// <summary>
    /// 查报警记录表
    /// </summary>
    public Task<RpcResponse> QueryHistoryAsync(RpcRequest request, CallContext context = default)
    {
        if (!request.TryGet<DateTime>(RpcParams.Start, out var start)
            || !request.TryGet<DateTime>(RpcParams.End, out var end))
        {
            return InvalidRequest();
        }

        string level = request.Get(RpcParams.Level, string.Empty);
        string keyword = request.Get(RpcParams.Keyword, string.Empty);
        int maxCount = request.Get(RpcParams.MaxCount, 0);
        return Task.Run(() =>
        {
            try
            {
                var configured = _alarms?.HistoryQueryMaxCount ?? 0;
                if (configured <= 0)
                {
                    configured = 1000;
                }

                var limit = maxCount > 0 ? Math.Min(maxCount, configured) : configured;

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
                    .Take(limit + 1)
                    .ToList();

                var result = new HistoryResult<AlarmHistoryDto>
                {
                    Items = rows.Take(limit).Select(row => _mapper.Map<AlarmHistoryDto>(row)).ToList(),
                    Truncated = rows.Count > limit,
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
