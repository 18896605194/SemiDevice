using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 工艺配方服务契约（配方 → 工艺配方页；流程配方页、腔体手动页选工艺配方也用它的列表）：编号 1~N 的工艺配方，
/// 看列表、看内容、看能选什么，新建、改名、删除、保存。
/// 没装工艺配方库（sc.xml 没配 ProcessRecipe 节点）时每个接口都回 process_recipe.not_installed。
/// </summary>
[ServiceContract]
public interface IProcessRecipeService
{
    /// <summary>
    /// 列表：编号个数 + 用了的编号（名称、说明、合计时长）。Data 为 ProcessRecipeListDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetListAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 一个工艺配方的全部内容。Data 为 ProcessRecipeDto 的 JSON。
    /// 失败回 process_recipe.index_out_of_range / process_recipe.not_found。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetAsync(ProcessRecipeIndexRequest request, CallContext context = default);

    /// <summary>
    /// 能选什么、范围多少（摆臂和药液来自 sc.xml，合计上限来自腔体工艺超时）。Data 为 ProcessRecipeOptionsDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetOptionsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 新建：空编号上建一个，内容按默认。Data 为建好的 ProcessRecipeDto 的 JSON。
    /// 失败回 process_recipe.index_out_of_range / process_recipe.index_occupied / process_recipe.name_* / process_recipe.save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateAsync(ProcessRecipeCreateRequest request, CallContext context = default);

    /// <summary>
    /// 重命名（版本加 1）。Data 为改完的 ProcessRecipeDto 的 JSON。
    /// 失败回 process_recipe.index_out_of_range / process_recipe.not_found / process_recipe.name_* / process_recipe.save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RenameAsync(ProcessRecipeRenameRequest request, CallContext context = default);

    /// <summary>
    /// 删除：编号空出来。失败回 process_recipe.index_out_of_range / process_recipe.not_found / process_recipe.save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteAsync(ProcessRecipeDeleteRequest request, CallContext context = default);

    /// <summary>
    /// 保存说明和步骤：版本对得上、步骤检查通过才存，存完版本加 1。Data 为存好的 ProcessRecipeDto 的 JSON。
    /// 失败回 process_recipe.index_out_of_range / process_recipe.not_found / process_recipe.revision_mismatch / process_recipe.no_steps /
    /// process_recipe.time_out_of_range / process_recipe.rpm_out_of_range / process_recipe.arm_not_found / process_recipe.chemical_required /
    /// process_recipe.chemical_not_on_arm / process_recipe.flow_out_of_range / process_recipe.position_out_of_range /
    /// process_recipe.scan_same_position / process_recipe.scan_speed_out_of_range / process_recipe.total_too_long / process_recipe.save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SaveAsync(ProcessRecipeSaveRequest request, CallContext context = default);
}
