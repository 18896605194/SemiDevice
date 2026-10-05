using System.Globalization;
using ProtoBuf.Grpc;
using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Jobs;

/// <summary>
/// Job gRPC 服务：把界面的请求转成 Job 管理的命令（IJobManager，跟以后 EAP 调的是同一个口子），结果翻成回包。
/// 校验、状态转换都在 Job 管理里做，这里只管取 Job 管理、转请求、转结果。
/// </summary>
public class JobService : BaseService, IJobService
{
    /// <summary>客户端没带操作人时记成这个。</summary>
    private const string UnknownOperator = "Unknown";

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

    public async Task<RpcResponse> CreateAsync(JobCreateRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return NotInstalled();
        }

        // protobuf 传输省略默认值字段，空字符串、空列表在接收端可能为 null。
        var slots = new Dictionary<int, string>();
        foreach (var slot in request.Slots ?? [])
        {
            slots[slot.Slot] = slot.Sequence ?? string.Empty;
        }

        string requestId = (request.RequestId ?? string.Empty).Trim();
        string operatorName = (request.Operator ?? string.Empty).Trim();
        var result = await jobs.CreateLocalJobAsync(new LocalJobRequest
        {
            LoadPort = request.LoadPort ?? string.Empty,
            LotId = request.LotId,
            SlotSequences = slots,
            AutoStart = request.AutoStart,
            RequestId = requestId.Length == 0 ? null : requestId,
            Operator = operatorName.Length == 0 ? UnknownOperator : operatorName,
        }).ConfigureAwait(false);

        return result.Accepted
            ? RpcResponse.Ok(JsonHelper.Serialize(new JobCreatedDto { ControlJob = result.JobId, ProcessJobs = result.ProcessJobs.ToList() }))
            : RpcResponse.Fail(result.Code, result.Args);
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

        var result = await jobs.CommandControlJobAsync(id, command, action, JobCommandSource.Local, RequestIdOf(request)).ConfigureAwait(false);
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

        var result = await jobs.CommandProcessJobAsync(id, command, JobCommandSource.Local, RequestIdOf(request)).ConfigureAwait(false);
        return Reply(result);
    }

    public async Task<RpcResponse> RecoverAsync(RpcRequest request, CallContext context = default)
    {
        var jobs = JobManager.Current;
        if (jobs is null)
        {
            return NotInstalled();
        }

        return Reply(await jobs.RecoverAsync(JobCommandSource.Local).ConfigureAwait(false));
    }

    private static string? RequestIdOf(JobCommandRequest request)
    {
        string id = (request.RequestId ?? string.Empty).Trim();
        return id.Length == 0 ? null : id;
    }

    private static RpcResponse Reply(JobCommandResult result)
    {
        return result.Accepted ? RpcResponse.Ok(JsonHelper.Serialize(result.JobId)) : RpcResponse.Fail(result.Code, result.Args);
    }

    private static RpcResponse NotInstalled()
    {
        return RpcResponse.Fail(ErrorCodes.JobNotInstalled, []);
    }
}
