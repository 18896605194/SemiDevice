using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// 晶圆账服务契约（设置 → 账单调整页）：机械手出错时实物和账对不上（比如片子已经在手指上，账还在腔体里），
/// 人按实物把账挪过去、删掉或补上。只改系统账，不让设备做任何动作。
/// </summary>
[ServiceContract]
public interface ILedgerService
{
    /// <summary>
    /// 账本全貌：能调整的位置和每个槽上的片。Data 为 WaferLedgerDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetLedgerAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> MoveAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> DeleteAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 调整记录
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    [OperationContract]
    Task<RpcResponse> GetAdjustmentsAsync(RpcRequest request, CallContext context = default);
}
