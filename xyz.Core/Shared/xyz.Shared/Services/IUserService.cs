using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 用户管理 gRPC 服务契约
/// </summary>
[ServiceContract]
public interface IUserService
{
    /// <summary>
    /// 全部账号（List&lt;UserDto&gt;）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetUsersAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 新建账号，回新建的 UserDto
    /// </summary>
    [OperationContract]
    Task<RpcResponse> CreateUserAsync(UserCreateRequest request, CallContext context = default);

    /// <summary>
    /// 改显示名、角色
    /// </summary>
    [OperationContract]
    Task<RpcResponse> UpdateUserAsync(UserUpdateRequest request, CallContext context = default);

    /// <summary>
    /// 重置密码（管理员用，不要旧密码）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ResetPasswordAsync(UserPasswordResetRequest request, CallContext context = default);

    /// <summary>
    /// 停用 / 启用
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetEnabledAsync(UserEnableRequest request, CallContext context = default);

    /// <summary>
    /// 删账号
    /// </summary>
    [OperationContract]
    Task<RpcResponse> DeleteUserAsync(AccountIdRequest request, CallContext context = default);
}
