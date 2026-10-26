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
    /// 参数：Index（int）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> GetStationGroupsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 参数：Index（int）、Name（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 参数：Index（int）、Name（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> RenameAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 参数：Index（int）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 参数：Index（int）、Revision（int，打开时读到的版本）、Steps（SequenceStepDto 列表）、Description（string，可不带）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SaveAsync(RpcRequest request, CallContext context = default);
}
