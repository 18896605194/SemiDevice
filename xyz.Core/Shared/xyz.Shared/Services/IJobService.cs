using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface IJobService
{
    [OperationContract]
    Task<RpcResponse> CreateProcessJobAsync(ProcessJobCreateRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> CancelProcessJobAsync(JobCommandRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> CreateControlJobAsync(ControlJobCreateRequest request, CallContext context = default);

    /// <summary>使用已有 PJ 名称集合创建 Job，内部创建 CJ 并关联这些 PJ。Data 为 JobCreatedDto。</summary>
    [OperationContract]
    Task<RpcResponse> CreateJobAsync(ControlJobCreateRequest request, CallContext context = default);

    /// <summary>取消尚未开始的 PJ；只使用 request.JobId。</summary>

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
    /// 出错的任务重做：退回待做，调度按片现在在哪重新派。不是出错的任务回 job.task_not_error。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RetryTaskAsync(JobTaskRequest request, CallContext context = default);

    /// <summary>
    /// 出错的任务标记完成：人已经把这一步做完了，接着走。取放要片在账上正好在这一步做完该在的地方，不在回 job.task_position_mismatch。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CompleteTaskAsync(JobTaskRequest request, CallContext context = default);
}
