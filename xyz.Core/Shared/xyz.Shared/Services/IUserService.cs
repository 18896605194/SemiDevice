using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 用户管理 gRPC 服务契约。
/// 请求统一使用 RpcRequest，返回统一使用 RpcResponse。
/// </summary>
[ServiceContract]
public interface IUserService
{
    [OperationContract]
    Task<RpcResponse> GetUsersAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> ExistsAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> CreateUserAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> DeleteUserAsync(RpcRequest request, CallContext context = default);
}
