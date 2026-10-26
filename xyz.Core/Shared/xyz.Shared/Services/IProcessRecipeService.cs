using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 工艺配方服务契约（配方 → 工艺配方页；流程配方页、腔体手动页选工艺配方也用它的列表）：编号 1~N 的工艺配方，
/// 看列表、看内容、看能选什么，新建、改名、删除、保存。
/// 没装工艺配方库（machine.xml 没配 ProcessRecipe 节点）时每个接口都回 process_recipe.not_installed。
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
    /// 获取工艺配方内容（ProcessRecipeDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 能选什么、范围多少（摆臂和药液来自 machine.xml，合计上限来自腔体工艺超时）。Data 为 ProcessRecipeOptionsDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetOptionsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 新建工艺配方（ProcessRecipeDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 重命名工艺配方（ProcessRecipeDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RenameAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 删除工艺配方
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 保存工艺配方的说明和步骤（ProcessRecipeDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SaveAsync(RpcRequest request, CallContext context = default);
}
