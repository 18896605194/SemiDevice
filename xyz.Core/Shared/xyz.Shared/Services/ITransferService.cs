using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 搬运服务契约：手动传片（跟 Job 自动调度走同一个搬运管理，完整走站点交互环、记账），以及出错后人工放锁。
/// 没装搬运管理（machine.xml 没配 Transfer 节点）时都回 transfer.not_installed。
/// </summary>
[ServiceContract]
public interface ITransferService
{
    /// <summary>
    /// 手动传片（TransferDoneDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> TransferAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 放开搬运失败保留的资源
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ReleaseAsync(RpcRequest request, CallContext context = default);
}
