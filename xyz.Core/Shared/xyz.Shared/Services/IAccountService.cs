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
    /// 登录：核对登录名和密码，成功回会话（SessionDto）。参数：UserName（string）、Password（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> LoginAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 取会话（SessionDto）：登录名为空取访客的；权限变了、重连以后用它刷新。参数：UserName（string，可不带，空 = 访客）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetSessionAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 注销：会话在客户端，后端只记一行日志。参数：UserName（string）、IsIdle（bool，可不带，是不是闲置自动注销）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> LogoutAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 改自己的密码（要旧密码）。参数：UserName（string）、OldPassword（string）、NewPassword（string）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> ChangePasswordAsync(RpcRequest request, CallContext context = default);
}
