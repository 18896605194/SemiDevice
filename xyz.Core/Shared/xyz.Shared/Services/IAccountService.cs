using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 登录 gRPC 服务契约
/// </summary>
[ServiceContract]
public interface IAccountService
{
    /// <summary>
    /// 登录：核对登录名和密码，成功回会话（SessionDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> LoginAsync(LoginRequest request, CallContext context = default);

    /// <summary>
    /// 取会话（SessionDto）：登录名为空取访客的；权限变了、重连以后用它刷新
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetSessionAsync(SessionRequest request, CallContext context = default);

    /// <summary>
    /// 注销：会话在客户端，后端只记一行日志
    /// </summary>
    [OperationContract]
    Task<RpcResponse> LogoutAsync(LogoutRequest request, CallContext context = default);

    /// <summary>
    /// 改自己的密码（要旧密码）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ChangePasswordAsync(PasswordChangeRequest request, CallContext context = default);
}
