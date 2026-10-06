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

/// <summary>
/// Job gRPC 服务：把界面的请求转成 Job 管理的命令（IJobManager，跟 EAP 调的是同样几个方法、过同一套检查），结果翻成回包。
/// 校验、状态转换都在 Job 管理里做，这里只管分 PJ、起名、转请求、转结果。
/// </summary>
public class JobService : BaseService, IJobService
{
    /// <summary>客户端没带操作人时记成这个。</summary>
    private const string UnknownOperator = "Unknown";

    private const string LogModule = "Job";

    public JobService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    public Task<RpcResponse> GetJobsAsync(RpcRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        return Task.FromResult(jobs is null
            ? NotInstalled()
            : RpcResponse.Ok(JsonHelper.Serialize(jobs.Snapshot)));
    }

    /// <summary>
    /// 本地建 Job，跟 Host 一样先建 PJ、再建 CJ 把 PJ 收进来：相同流程配方的槽分成一个 PJ（PJ 的先后、PJ 里片的先后都按 LoadPort 的取片顺序），
    /// 整篮一个 CJ。CJ 名用批次号，没填就自动起（CJ-LoadPort-时间）；PJ 名是 CJ 名加序号。中途哪一步被拒，已经建好的 PJ 撤掉，回被拒的原因。
    /// </summary>
    public async Task<RpcResponse> CreateAsync(JobCreateRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return NotInstalled();
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
            var spec = new ProcessJobSpec
            {
                Id = $"{controlId}-{(created.Count + 1).ToString(CultureInfo.InvariantCulture)}",
                LoadPort = port.Name,
                Slots = slots,
                Sequence = sequence,
            };
            var process = await jobs.CreateProcessJobAsync(spec).ConfigureAwait(false);
            if (!process.IsSuccess)
            {
                await CancelAsync(jobs, created).ConfigureAwait(false);
                return Reply(process);
            }

            created.Add(spec.Id);
        }

        var control = await jobs.CreateControlJobAsync(new ControlJobSpec
        {
            Id = controlId,
            ProcessJobs = created,
            AutoStart = request.AutoStart,
            LotId = lotId.Length > 0 ? lotId : null,
        }).ConfigureAwait(false);
        if (!control.IsSuccess)
        {
            await CancelAsync(jobs, created).ConfigureAwait(false);
            return Reply(control);
        }

        string operatorName = (request.Operator ?? string.Empty).Trim();
        LogHelper.Info(LogModule, $"建 Job {controlId}（{port.Name}，{created.Count} 个 PJ，{groups.Sum(group => group.Slots.Count)} 片，"
            + $"操作人 {(operatorName.Length == 0 ? UnknownOperator : operatorName)}）");
        return RpcResponse.Ok(JsonHelper.Serialize(new JobCreatedDto { ControlJob = controlId, ProcessJobs = created }));
    }

    public async Task<RpcResponse> ControlJobCommandAsync(JobCommandRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return NotInstalled();
        }

        string id = (request.JobId ?? string.Empty).Trim();
        var command = (CtrlJobCommand)request.Command;
        var action = (CtrlJobAction)request.Action;
        if (!Enum.IsDefined(command) || !Enum.IsDefined(action))
        {
            return RpcResponse.Fail(ErrorCodes.JobCommandNotAllowed,
                [id, request.Command.ToString(CultureInfo.InvariantCulture), string.Empty]);
        }

        var result = await jobs.CommandControlJobAsync(id, command, action).ConfigureAwait(false);
        return Reply(result);
    }

    public async Task<RpcResponse> ProcessJobCommandAsync(JobCommandRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return NotInstalled();
        }

        string id = (request.JobId ?? string.Empty).Trim();
        var command = (PrJobCommand)request.Command;
        if (!Enum.IsDefined(command))
        {
            return RpcResponse.Fail(ErrorCodes.JobCommandNotAllowed,
                [id, request.Command.ToString(CultureInfo.InvariantCulture), string.Empty]);
        }

        var result = await jobs.CommandProcessJobAsync(id, command).ConfigureAwait(false);
        return Reply(result);
    }

    public async Task<RpcResponse> RetryTaskAsync(JobTaskRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return NotInstalled();
        }

        return Reply(await jobs.RetryTaskAsync(request.ProcessJob ?? string.Empty, request.Slot, request.Task).ConfigureAwait(false));
    }

    public async Task<RpcResponse> CompleteTaskAsync(JobTaskRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return NotInstalled();
        }

        return Reply(await jobs.CompleteTaskAsync(request.ProcessJob ?? string.Empty, request.Slot, request.Task).ConfigureAwait(false));
    }

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
            var result = await jobs.CommandProcessJobAsync(id, PrJobCommand.Cancel).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                LogHelper.Warn(LogModule, $"建 Job 没成，已经建好的 PJ {id} 没撤掉（{result.ErrorMessage}），要手动取消");
            }
        }
    }

    private static RpcResponse Reply(HandleResult result)
    {
        return result.IsSuccess ? RpcResponse.Ok(JsonHelper.Serialize(result.Result)) : RpcResponse.Fail(result.ErrorMessage, result.Args);
    }

    private static RpcResponse NotInstalled()
    {
        return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
    }
}
