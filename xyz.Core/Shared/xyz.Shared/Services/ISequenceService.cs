using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 流程配方服务
/// </summary>
[ServiceContract]
public interface ISequenceService
{
    [OperationContract]
    Task<RpcResponse> GetListAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 获取流程配方
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> GetStationGroupsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 新建流程配方
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 重命名流程配方
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RenameAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 删除流程配方
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 保存流程配方
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SaveAsync(RpcRequest request, CallContext context = default);
}
