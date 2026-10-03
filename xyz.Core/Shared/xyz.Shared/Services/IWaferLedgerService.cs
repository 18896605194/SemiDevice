using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 晶圆账服务契约（设置 → 账单调整页）：机械手出错时实物和账对不上（比如片子已经在手指上，账还在腔体里），
/// 人按实物把账挪过去、删掉或补上。只改系统账，不让设备做任何动作。
/// </summary>
[ServiceContract]
public interface IWaferLedgerService
{
    /// <summary>
    /// 账本全貌：能调整的位置和每个槽上的片。Data 为 WaferLedgerDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetLedgerAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 人工移账：源必须有片、目标必须是空槽，片号、批次、载具号、工艺状态跟着片走；照常记晶圆流水，另记一条调整记录。
    /// 失败回 wafer.ledger_disabled / wafer.location_not_found / wafer.slot_out_of_range / wafer.no_wafer /
    /// wafer.slot_occupied / wafer.same_slot。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> MoveAsync(WaferMoveRequest request, CallContext context = default);

    /// <summary>
    /// 人工删账：这个槽上必须有片；照常记晶圆流水，另记一条调整记录。
    /// 失败回 wafer.ledger_disabled / wafer.location_not_found / wafer.slot_out_of_range / wafer.no_wafer。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteAsync(WaferDeleteRequest request, CallContext context = default);

    /// <summary>
    /// 人工补账：在空槽上建一片，片号必填，其余按默认（正常片、未处理）；片号已经在账上的拒绝。照常记晶圆流水，另记一条调整记录。
    /// 失败回 wafer.ledger_disabled / wafer.location_not_found / wafer.slot_out_of_range / wafer.slot_occupied /
    /// wafer.id_required / wafer.duplicate_id。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateAsync(WaferCreateRequest request, CallContext context = default);

    /// <summary>
    /// 最近的人工调整记录，新的在前，最多 50 条。Data 为 List&lt;WaferAdjustmentDto&gt; 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetAdjustmentsAsync(RpcRequest request, CallContext context = default);
}
