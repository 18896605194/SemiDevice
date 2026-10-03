using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 流程配方服务契约（配方 → 流程配方页）：编号 1~N 的流程配方，看列表、看内容、新建、改名、删除、保存。
/// 没装流程配方库（sc.xml 没配 Sequence 节点）时每个接口都回 sequence.not_installed。
/// </summary>
[ServiceContract]
public interface ISequenceService
{
    /// <summary>
    /// 列表：编号个数 + 用了的编号和名称。Data 为 SequenceListDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetListAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 一个流程配方的全部内容。Data 为 SequenceDto 的 JSON。
    /// 失败回 sequence.index_out_of_range / sequence.not_found。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetAsync(SequenceIndexRequest request, CallContext context = default);

    /// <summary>
    /// 可选的站点分组（来自 sc.xml 的分组节点和装起来的模块）。Data 为 List&lt;SequenceStationGroupDto&gt; 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetStationGroupsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 新建：空编号上建一个，内容按默认。Data 为建好的 SequenceDto 的 JSON。
    /// 失败回 sequence.index_out_of_range / sequence.index_occupied / sequence.name_* / sequence.save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateAsync(SequenceCreateRequest request, CallContext context = default);

    /// <summary>
    /// 重命名（版本加 1）。Data 为改完的 SequenceDto 的 JSON。
    /// 失败回 sequence.index_out_of_range / sequence.not_found / sequence.name_* / sequence.save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RenameAsync(SequenceRenameRequest request, CallContext context = default);

    /// <summary>
    /// 删除：编号空出来。失败回 sequence.index_out_of_range / sequence.not_found / sequence.save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteAsync(SequenceDeleteRequest request, CallContext context = default);

    /// <summary>
    /// 保存说明和步骤：版本对得上、步骤检查通过才存，存完版本加 1。Data 为存好的 SequenceDto 的 JSON。
    /// 失败回 sequence.index_out_of_range / sequence.not_found / sequence.revision_mismatch / sequence.too_few_steps /
    /// sequence.step_not_loadport / sequence.group_not_found / sequence.station_required / sequence.station_not_in_group /
    /// sequence.recipe_required / sequence.save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SaveAsync(SequenceSaveRequest request, CallContext context = default);
}
