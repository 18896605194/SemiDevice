using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface IJobService
{
    /// <summary>
    /// CJ 命令值 Start（E94 CTLJOBCMD 1）
    /// </summary>
    const int ControlJobStart = 1;

    /// <summary>
    /// 建独立 PJ，建好后等 CJ 关联。参数：LoadPort（string）、Slots（int 列表）、Sequence（string）、Name（string，可不带）、LotId（string，可不带）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateProcessJobAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 取消还没开始的 PJ。参数：JobId（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CancelProcessJobAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 关联已经建好的 PJ 建 CJ；Name 为空时用 LotId 或自动起名。
    /// 参数：LoadPort（string）、ProcessJobs（string 列表）、Name（string，可不带）、LotId（string，可不带）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateControlJobAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 使用已有 PJ 名称集合创建 Job，内部创建 CJ 并关联这些 PJ。Data 为 JobCreatedDto。参数同 <see cref="CreateControlJobAsync"/>
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateJobAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// Job 全貌（跟推送是同一份）。Data 为 JobListDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetJobsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 按建的时刻查库里的 CJ。Data 为 HistoryResult&lt;ControlJobHistoryDto&gt; 的 JSON（最新的在前）。
    /// 参数：Start（DateTime，含）、End（DateTime，不含）、Keyword（string，可不带）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryHistoryAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 库里一个 CJ 下面的 PJ（每片带它的一行任务），按建的先后；Data 为 ProcessJobDto 数组的 JSON。参数：ControlJob（string，CJ 的行号）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetHistoryProcessJobsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 本地建 Job：一个 LoadPort 上的一篮，每槽一个流程配方。Data 为 JobCreatedDto 的 JSON。
    /// 不建回 job.* 的码（LoadPort、载具、片、名字、上限、流程配方、工艺配方、站点、回片槽）。
    /// 参数：LoadPort（string）、Slots（JobSlotDto 列表）、LotId（string，可不带，空就自动起名）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// CJ 命令（命令值照 E94：1 Start … 8 HOQ；Action 0 SaveJobs、1 RemoveJobs）。参数：JobId（string）、Command（int）、Action（int，可不带）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ControlJobCommandAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// PJ 命令（0 Start、1 Pause、2 Resume、3 Stop、4 Abort、5 Cancel）。参数：JobId（string）、Command（int）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ProcessJobCommandAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 出错的任务重做：退回待做，调度按片现在在哪重新派。不是出错的任务回 job.task_not_error。
    /// 参数：ProcessJob（string）、Slot（int，来源槽号）、Task（int，这一片的第几个任务，从 0 开始）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RetryTaskAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 出错的任务标记完成：人已经把这一步做完了，接着走。取放要片在账上正好在这一步做完该在的地方，不在回 job.task_position_mismatch。
    /// 参数同 <see cref="RetryTaskAsync"/>
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CompleteTaskAsync(RpcRequest request, CallContext context = default);
}
