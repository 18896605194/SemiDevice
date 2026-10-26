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

    [OperationContract]
    Task<RpcResponse> GetAsync(SequenceIndexRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> GetStationGroupsAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> CreateAsync(SequenceCreateRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> RenameAsync(SequenceRenameRequest request, CallContext context = default);


    [OperationContract]
    Task<RpcResponse> DeleteAsync(SequenceDeleteRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> SaveAsync(SequenceSaveRequest request, CallContext context = default);
}
