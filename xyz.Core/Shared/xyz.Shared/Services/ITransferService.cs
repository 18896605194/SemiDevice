using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 搬运服务契约：手动传片（跟 Job 自动调度走同一个搬运管理，完整走站点交互环、记账），以及出错后人工放锁。
/// 没装搬运管理（sc.xml 没配 Transfer 节点）时都回 transfer.not_installed。
/// </summary>
[ServiceContract]
public interface ITransferService
{
    /// <summary>
    /// 手动传片：下一张搬运单并等它做完（上限是搬运管理的 EC ManualWaitTimeoutMs）。Data 为 TransferDoneDto 的 JSON。
    /// 受理不了回 transfer.* / wafer.* 的码（站点、槽位、片、被 Job 占着、锁、机械手、手臂）；
    /// 没搬成回执行时的码（transfer.station_busy、transfer.failed……）；等超时回 transfer.wait_timeout（搬运还在跑）。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> TransferAsync(TransferRequestDto request, CallContext context = default);

    /// <summary>
    /// 放开一张出错搬运单留着的锁（动过手才失败的单，片位要人工确认）。没有这张留着的单回 transfer.not_held。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ReleaseAsync(TransferReleaseRequest request, CallContext context = default);
}
