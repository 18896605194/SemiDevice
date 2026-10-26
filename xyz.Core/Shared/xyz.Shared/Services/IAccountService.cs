using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 登录服务
/// </summary>
[ServiceContract]
public interface IAccountService
{
    [OperationContract]
    Task<RpcResponse> LoginAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 获取会话（SessionDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetSessionAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 注销
    /// </summary>
    [OperationContract]
    Task<RpcResponse> LogoutAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 修改密码
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ChangePasswordAsync(RpcRequest request, CallContext context = default);
}
