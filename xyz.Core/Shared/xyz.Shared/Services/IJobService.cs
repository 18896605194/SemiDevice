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
    /// 创建独立 PJ
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateProcessJobAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 取消还没开始的 PJ
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CancelProcessJobAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 关联已建好的 PJ 创建 CJ
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateControlJobAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 用已有 PJ 创建 Job（JobCreatedDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateJobAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// Job 全貌（跟推送是同一份）。Data 为 JobListDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetJobsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 按创建时间查询 CJ 历史（HistoryResult&lt;ControlJobHistoryDto&gt;）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryHistoryAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 获取库里某个 CJ 下的 PJ（ProcessJobDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetHistoryProcessJobsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 本地创建 Job（JobCreatedDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 下发 CJ 命令
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ControlJobCommandAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 下发 PJ 命令
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ProcessJobCommandAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 重做出错的任务
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RetryTaskAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 出错的任务标记完成
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CompleteTaskAsync(RpcRequest request, CallContext context = default);
}
