using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// Job 服务契约：本地建 Job、CJ / PJ 命令（照 SEMI E94 / E40）、恢复派单、查全貌。
/// Job 的进展不用轮询：JobListDto 推送（token = JobListDto.EventToken，留存）。没装 Job 管理时都回 job.not_installed。
/// </summary>
[ServiceContract]
public interface IJobService
{
    /// <summary>Job 全貌（跟推送是同一份）。Data 为 JobListDto 的 JSON。</summary>
    [OperationContract]
    Task<RpcResponse> GetJobsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 本地建 Job：一个 LoadPort 上的一篮，每槽一个流程配方。Data 为 JobCreatedDto 的 JSON。
    /// 不建回 job.* 的码（LoadPort、载具、片、名字、上限、流程配方、工艺配方、站点、回片槽）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateAsync(JobCreateRequest request, CallContext context = default);

    /// <summary>CJ 命令（命令值照 E94：1 Start … 8 HOQ；Action 0 SaveJobs、1 RemoveJobs）。</summary>
    [OperationContract]
    Task<RpcResponse> ControlJobCommandAsync(JobCommandRequest request, CallContext context = default);

    /// <summary>PJ 命令（0 Start、1 Pause、2 Resume、3 Stop、4 Abort、5 Cancel）。</summary>
    [OperationContract]
    Task<RpcResponse> ProcessJobCommandAsync(JobCommandRequest request, CallContext context = default);

    /// <summary>
    /// 恢复派单：出过执行故障、自动派单暂停后，到现场确认过片位、对好账再调。还有出错的搬运单片位没确认回 job.recovery_pending。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RecoverAsync(RpcRequest request, CallContext context = default);
}
