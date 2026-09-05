using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 菜单 gRPC 服务契约。
/// </summary>
[ServiceContract]
public interface IMenuService
{
    [OperationContract]
    Task<RpcResponse> GetMenusAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> CreateMenuAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> SaveMenuAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> DeleteMenuAsync(RpcRequest request, CallContext context = default);
}
