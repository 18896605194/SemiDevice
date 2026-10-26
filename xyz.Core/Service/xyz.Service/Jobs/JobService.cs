using System.Globalization;
using ProtoBuf.Grpc;
using SqlSugar;
using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Components;
using xyz.Database.DbProvider;
using xyz.Database.Jobs;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Jobs;

public class JobService : BaseService, IJobService
{
    #region 基础成员

    private readonly IJobManager? _jobs;

    private readonly IMapper _mapper;

    public JobService()
    {
        _jobs = IocHelper.GetService<IJobManager>();
        _mapper = IocHelper.GetService<IMapper>() ?? throw new InvalidOperationException("machine.xml 没配映射组件（Mapper）");
    }

    #endregion

    #region Job 查询

    public Task<RpcResponse> GetJobsAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.JobNotInstalled, []));
        }

        string json = JsonHelper.Serialize(_jobs.Snapshot);
        return Task.FromResult(RpcResponse.Ok(json));
    }

    /// <summary>
    /// 查 control_job 表：建的时刻在时间段里的 CJ，再按关键字筛，最新的在前。读库放线程池
    /// </summary>
    public Task<RpcResponse> QueryHistoryAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.JobNotInstalled, []));
        }

        if (!request.TryGet<DateTime>(RpcParams.Start, out var start)
            || !request.TryGet<DateTime>(RpcParams.End, out var end))
        {
            return InvalidRequest();
        }

        string keyword = request.Get(RpcParams.Keyword, string.Empty).Trim();
        var jobs = _jobs;
        return Task.Run(() =>
        {
            try
            {
                int maxCount = jobs.HistoryQueryMaxCount;
                using var db = XyzDb.Create(jobs.Database);
                if (!db.DbMaintenance.IsAnyTable(db.EntityMaintenance.GetTableName<ControlJobEntity>(), false))
                {
                    return RpcResponse.Ok(JsonHelper.Serialize(new HistoryResult<ControlJobHistoryDto>()));
                }

                var rows = db.Queryable<ControlJobEntity>()
                    .Where(row => row.CreatedTime >= start && row.CreatedTime < end)
                    .WhereIF(keyword.Length > 0, row => row.Name.Contains(keyword) || row.LotId.Contains(keyword)
                        || row.CarrierId.Contains(keyword) || row.LoadPort.Contains(keyword))
                    .OrderBy(row => row.CreatedTime, OrderByType.Desc)
                    .OrderBy(row => row.Id, OrderByType.Desc)
                    .Take(maxCount + 1)
                    .ToList();

                var result = new HistoryResult<ControlJobHistoryDto>
                {
                    Items = rows.Take(maxCount).Select(row => _mapper.Map<ControlJobHistoryDto>(row)).ToList(),
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

    /// <summary>
    /// 查 process_job 表里归这个 CJ（按行号）的 PJ，按建的先后。读库放线程池
    /// </summary>
    public Task<RpcResponse> GetHistoryProcessJobsAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.JobNotInstalled, []));
        }

        if (!request.TryGet<string>(RpcParams.ControlJob, out var text))
        {
            return InvalidRequest();
        }

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long controlJob))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.JobNotFound, [text]));
        }

        var jobs = _jobs;
        return Task.Run(() =>
        {
            try
            {
                using var db = XyzDb.Create(jobs.Database);
                if (!db.DbMaintenance.IsAnyTable(db.EntityMaintenance.GetTableName<ProcessJobEntity>(), false))
                {
                    return RpcResponse.Ok(JsonHelper.Serialize(new List<ProcessJobDto>()));
                }

                var rows = db.Queryable<ProcessJobEntity>()
                    .Where(row => row.ControlJobRowId == controlJob)
                    .OrderBy(row => row.CreatedTime)
                    .OrderBy(row => row.Id)
                    .ToList();
                var processJobs = rows.Select(row => _mapper.Map<ProcessJobDto>(row)).ToList();
                return RpcResponse.Ok(JsonHelper.Serialize(processJobs));
            }
            catch (Exception exception)
            {
                return RpcResponse.Fail(ErrorCodes.HistoryQueryFailed, [exception.Message]);
            }
        });
    }

    #endregion

    #region Job 创建

    /// <summary>
    /// 创建pj
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<RpcResponse> CreateProcessJobAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.LoadPort, out var loadPort)
            || !request.TryGet<List<int>>(RpcParams.Slots, out var slots)
            || !request.TryGet<string>(RpcParams.Sequence, out var sequence))
        {
            return await InvalidRequest();
        }

        string name = request.Get(RpcParams.Name, string.Empty);
        string lotId = request.Get(RpcParams.LotId, string.Empty);
        var result = await _jobs.CreateProcessJobAsync(
            loadPort,
            name,
            slots,
            sequence,
            lotId).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    /// <summary>
    /// 创建cj
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<RpcResponse> CreateControlJobAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.LoadPort, out var loadPort)
            || !request.TryGet<List<string>>(RpcParams.ProcessJobs, out var processJobs))
        {
            return await InvalidRequest();
        }

        string name = request.Get(RpcParams.Name, string.Empty);
        string lotId = request.Get(RpcParams.LotId, string.Empty);
        var result = await _jobs.CreateControlJobAsync(
            loadPort,
            processJobs,
            name,
            lotId).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    /// <summary>
    /// 创建job
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<RpcResponse> CreateJobAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.LoadPort, out var loadPort)
            || !request.TryGet<List<string>>(RpcParams.ProcessJobs, out var processJobs))
        {
            return await InvalidRequest();
        }

        string name = request.Get(RpcParams.Name, string.Empty);
        string lotId = request.Get(RpcParams.LotId, string.Empty);
        var result = await _jobs.CreateJobAsync(
            loadPort,
            processJobs,
            name,
            lotId).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    /// <summary>
    /// 创建job
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.LoadPort, out var loadPort)
            || !request.TryGet<List<JobSlotDto>>(RpcParams.Slots, out var jobSlots))
        {
            return await InvalidRequest();
        }

        string portName = loadPort.Trim();
        var port = FindModule<ILoadPort>(portName);
        if (port is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobLoadPortNotFound, [portName]);
        }

        var groups = GroupBySequence(jobSlots, port.PickOrder);
        if (groups.Count == 0)
        {
            return RpcResponse.Fail(ErrorCodes.JobNoWafers, [port.Name]);
        }

        string lotId = request.Get(RpcParams.LotId, string.Empty).Trim();
        string controlId = lotId.Length > 0
            ? lotId
            : $"CJ-{port.Name}-{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
        var created = new List<string>();
        foreach (var (sequence, slots) in groups)
        {
            string processId = $"{controlId}-{(created.Count + 1).ToString(CultureInfo.InvariantCulture)}";
            var process = await _jobs.CreateProcessJobAsync(
                port.Name, processId, slots, sequence, lotId).ConfigureAwait(false);
            if (!process.IsSuccess)
            {
                await CancelAsync(_jobs, created).ConfigureAwait(false);
                return RpcResponse.Fail(process.ErrorMessage, process.Args);
            }

            created.Add(processId);
        }

        var control = await _jobs.CreateJobAsync(
            port.Name, created, controlId, lotId).ConfigureAwait(false);
        if (!control.IsSuccess)
        {
            await CancelAsync(_jobs, created).ConfigureAwait(false);
            return RpcResponse.Fail(control.ErrorMessage, control.Args);
        }

        LogJobInfo($"建 Job {controlId}（{port.Name}，{created.Count} 个 PJ，{groups.Sum(group => group.Slots.Count)} 片，"
            + $"操作人 {RpcOperator.Of(context)}）");
        string json = JsonHelper.Serialize(control.Result);
        return RpcResponse.Ok(json);
    }

    #endregion

    #region CJ 与 PJ 控制

    public async Task<RpcResponse> CancelProcessJobAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.JobId, out var jobId))
        {
            return await InvalidRequest();
        }

        var result = await _jobs.CancelProcessJobAsync(jobId).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    /// <summary>
    /// CJ 指令操作
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<RpcResponse> ControlJobCommandAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.JobId, out var jobId)
            || !request.TryGet<int>(RpcParams.Command, out var commandValue))
        {
            return await InvalidRequest();
        }

        int actionValue = request.Get(RpcParams.Action, 0);
        string id = jobId.Trim();
        var command = (ControlJobCommand)commandValue;
        var action = (ControlJobAction)actionValue;
        if (!Enum.IsDefined(command) || !Enum.IsDefined(action))
        {
            return RpcResponse.Fail(ErrorCodes.JobCommandNotAllowed,
                [id, commandValue.ToString(CultureInfo.InvariantCulture), string.Empty]);
        }

        var result = await _jobs.ExecuteControlJobCommandAsync(id, command, action).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    public async Task<RpcResponse> ProcessJobCommandAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.JobId, out var jobId)
            || !request.TryGet<int>(RpcParams.Command, out var commandValue))
        {
            return await InvalidRequest();
        }

        string id = jobId.Trim();
        var command = (ProcessJobCommand)commandValue;
        if (!Enum.IsDefined(command))
        {
            return RpcResponse.Fail(ErrorCodes.JobCommandNotAllowed,
                [id, commandValue.ToString(CultureInfo.InvariantCulture), string.Empty]);
        }

        var result = await _jobs.ExecuteProcessJobCommandAsync(id, command).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    #endregion

    #region 任务恢复

    public async Task<RpcResponse> RetryTaskAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.ProcessJob, out var processJob)
            || !request.TryGet<int>(RpcParams.Slot, out var slot)
            || !request.TryGet<int>(RpcParams.Task, out var task))
        {
            return await InvalidRequest();
        }

        var result = await _jobs.RetryTaskAsync(processJob, slot, task).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    public async Task<RpcResponse> CompleteTaskAsync(RpcRequest request, CallContext context = default)
    {
        if (_jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.ProcessJob, out var processJob)
            || !request.TryGet<int>(RpcParams.Slot, out var slot)
            || !request.TryGet<int>(RpcParams.Task, out var task))
        {
            return await InvalidRequest();
        }

        var result = await _jobs.CompleteTaskAsync(processJob, slot, task).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 选了流程配方的槽按配方分组（不分大小写），按 LoadPort 的取片顺序：组的先后看组里最先取的那槽，组里的槽也照这个顺序。
    /// </summary>
    private static List<(string Sequence, List<int> Slots)> GroupBySequence(IEnumerable<JobSlotDto> slots, SlotPickOrder order)
    {
        var selected = new Dictionary<int, string>();
        foreach (var slot in slots)
        {
            string sequence = (slot.Sequence ?? string.Empty).Trim();
            if (sequence.Length > 0)
            {
                selected[slot.Slot] = sequence;
            }
        }

        var ordered = order == SlotPickOrder.TopDown
            ? selected.OrderByDescending(pair => pair.Key)
            : selected.OrderBy(pair => pair.Key);
        return ordered
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, group.Select(pair => pair.Key).ToList()))
            .ToList();
    }

    /// <summary>建 Job 中途被拒：已经建好的 PJ 撤掉（还在排队的 PJ 收到取消就删，E40 #18）。</summary>
    private async Task CancelAsync(IJobManager jobs, IEnumerable<string> processJobs)
    {
        foreach (string id in processJobs)
        {
            var result = await jobs.CancelProcessJobAsync(id).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                LogJobWarn($"建 Job 没成，已经建好的 PJ {id} 没撤掉（{result.ErrorMessage}），要手动取消");
            }
        }
    }

    /// <summary>
    /// Job 的日志记在 Job 管理名下（它是组件）；不是组件的（冒烟里的假 Job 管理）记在 Host 下
    /// </summary>
    private void LogJobInfo(string message)
    {
        if (_jobs is ILogSource source)
        {
            LogHelper.Info(source, message);
            return;
        }

        LogHelper.Info(LogHelper.HostModule, message);
    }

    /// <summary>
    /// 同 <see cref="LogJobInfo"/>，记告警
    /// </summary>
    private void LogJobWarn(string message)
    {
        if (_jobs is ILogSource source)
        {
            LogHelper.Warn(source, message);
            return;
        }

        LogHelper.Warn(LogHelper.HostModule, message);
    }

    #endregion
}
