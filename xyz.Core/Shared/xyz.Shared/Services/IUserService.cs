using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

[ServiceContract]
public interface IUserService
{
    [OperationContract]
    Task<RpcResponse> GetUsersAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> CreateUserAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> UpdateUserAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> ResetPasswordAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> SetEnabledAsync(RpcRequest request, CallContext context = default);
    [OperationContract]
    Task<RpcResponse> DeleteUserAsync(RpcRequest request, CallContext context = default);
}
