using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 角色管理 gRPC 服务契约。
/// 请求统一使用 RpcRequest，返回统一使用 RpcResponse。
/// </summary>
[ServiceContract]
public interface IRoleService
{
    [OperationContract]
    Task<RpcResponse> GetRolesAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> ExistsAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> CreateRoleAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> DeleteRoleAsync(RpcRequest request, CallContext context = default);
}
