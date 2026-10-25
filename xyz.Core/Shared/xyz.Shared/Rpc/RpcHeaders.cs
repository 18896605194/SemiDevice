namespace xyz.Shared.Rpc;

/// <summary>
/// 客户端每次调后端都带的请求头
/// </summary>
public static class RpcHeaders
{
    /// <summary>
    /// 操作人（登录名，没登录是 Guest，没启用登录是 Admin）：UTF-8 字节，登录名可能是中文，普通请求头只能放 ASCII，所以用 -bin 结尾的二进制头
    /// </summary>
    public const string Operator = "xyz-user-bin";
}
