using System.Globalization;
using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Jobs;

public class JobService : BaseService, IJobService
{
    #region 基础成员

    /// <summary>客户端没带操作人时记成这个。</summary>
    private const string UnknownOperator = "Unknown";

    private const string LogModule = "Job";

    public JobService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    #endregion

    #region Job 查询

    public Task<RpcResponse> GetJobsAsync(RpcRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.JobNotInstalled, []));
        }

        string json = JsonHelper.Serialize(jobs.Snapshot);
        return Task.FromResult(RpcResponse.Ok(json));
    }

    #endregion

    #region Job 创建

    /// <summary>
    /// 创建pj
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<RpcResponse> CreateProcessJobAsync(ProcessJobCreateRequest request, CallContext context = default)
    {
        var _jobManger = JobManager.Current;
        if (_jobManger is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        var result = await _jobManger.CreateProcessJobAsync(
            request.LoadPort ?? string.Empty,
            request.Name ?? string.Empty,
            request.Slots ?? [],
            request.Sequence ?? string.Empty,
            request.LotId).ConfigureAwait(false);

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
    public async Task<RpcResponse> CreateControlJobAsync(ControlJobCreateRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        var result = await jobs.CreateControlJobAsync(
            request.LoadPort ?? string.Empty,
            request.ProcessJobs ?? [],
            request.Name,
            request.LotId).ConfigureAwait(false);

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
    public async Task<RpcResponse> CreateJobAsync(ControlJobCreateRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        var result = await jobs.CreateJobAsync(
            request.LoadPort ?? string.Empty,
            request.ProcessJobs ?? [],
            request.Name,
            request.LotId).ConfigureAwait(false);

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
    public async Task<RpcResponse> CreateAsync(JobCreateRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        // protobuf 传输省略默认值字段，空字符串、空列表在接收端可能为 null。
        string portName = (request.LoadPort ?? string.Empty).Trim();
        var port = FindModule<BaseLoadPortModule>(portName);
        if (port is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobLoadPortNotFound, [portName]);
        }

        var groups = GroupBySequence(request.Slots ?? [], port.PickOrder);
        if (groups.Count == 0)
        {
            return RpcResponse.Fail(ErrorCodes.JobNoWafers, [port.Name]);
        }

        string lotId = (request.LotId ?? string.Empty).Trim();
        string controlId = lotId.Length > 0
            ? lotId
            : $"CJ-{port.Name}-{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
        var created = new List<string>();
        foreach (var (sequence, slots) in groups)
        {
            string processId = $"{controlId}-{(created.Count + 1).ToString(CultureInfo.InvariantCulture)}";
            var process = await jobs.CreateProcessJobAsync(
                port.Name, processId, slots, sequence, lotId).ConfigureAwait(false);
            if (!process.IsSuccess)
            {
                await CancelAsync(jobs, created).ConfigureAwait(false);
                return RpcResponse.Fail(process.ErrorMessage, process.Args);
            }

            created.Add(processId);
        }

        var control = await jobs.CreateJobAsync(
            port.Name, created, controlId, lotId).ConfigureAwait(false);
        if (!control.IsSuccess)
        {
            await CancelAsync(jobs, created).ConfigureAwait(false);
            return RpcResponse.Fail(control.ErrorMessage, control.Args);
        }

        string operatorName = (request.Operator ?? string.Empty).Trim();
        LogHelper.Info(LogModule, $"建 Job {controlId}（{port.Name}，{created.Count} 个 PJ，{groups.Sum(group => group.Slots.Count)} 片，"
            + $"操作人 {(operatorName.Length == 0 ? UnknownOperator : operatorName)}）");
        string json = JsonHelper.Serialize(control.Result);
        return RpcResponse.Ok(json);
    }

    #endregion

    #region CJ 与 PJ 控制

    public async Task<RpcResponse> CancelProcessJobAsync(JobCommandRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        var result = await jobs.CancelProcessJobAsync(request.JobId ?? string.Empty).ConfigureAwait(false);
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
    public async Task<RpcResponse> ControlJobCommandAsync(JobCommandRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        string id = (request.JobId ?? string.Empty).Trim();
        var command = (ControlJobCommand)request.Command;
        var action = (ControlJobAction)request.Action;
        if (!Enum.IsDefined(command) || !Enum.IsDefined(action))
        {
            return RpcResponse.Fail(ErrorCodes.JobCommandNotAllowed,
                [id, request.Command.ToString(CultureInfo.InvariantCulture), string.Empty]);
        }

        var result = await jobs.ExecuteControlJobCommandAsync(id, command, action).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    public async Task<RpcResponse> ProcessJobCommandAsync(JobCommandRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        string id = (request.JobId ?? string.Empty).Trim();
        var command = (ProcessJobCommand)request.Command;
        if (!Enum.IsDefined(command))
        {
            return RpcResponse.Fail(ErrorCodes.JobCommandNotAllowed,
                [id, request.Command.ToString(CultureInfo.InvariantCulture), string.Empty]);
        }

        var result = await jobs.ExecuteProcessJobCommandAsync(id, command).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    #endregion

    #region 任务恢复

    public async Task<RpcResponse> RetryTaskAsync(JobTaskRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        var result = await jobs.RetryTaskAsync(request.ProcessJob ?? string.Empty, request.Slot, request.Task).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            string json = JsonHelper.Serialize(result.Result);
            return RpcResponse.Ok(json);
        }

        return RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    public async Task<RpcResponse> CompleteTaskAsync(JobTaskRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
        }

        var result = await jobs.CompleteTaskAsync(request.ProcessJob ?? string.Empty, request.Slot, request.Task).ConfigureAwait(false);
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
    private static async Task CancelAsync(IJobManager jobs, IEnumerable<string> processJobs)
    {
        foreach (string id in processJobs)
        {
            var result = await jobs.CancelProcessJobAsync(id).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                LogHelper.Warn(LogModule, $"建 Job 没成，已经建好的 PJ {id} 没撤掉（{result.ErrorMessage}），要手动取消");
            }
        }
    }

    #endregion
}
