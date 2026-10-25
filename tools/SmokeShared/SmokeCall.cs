using System.Text;
using Grpc.Core;
using ProtoBuf.Grpc;
using xyz.Shared.Rpc;

/// <summary>
/// 冒烟里直接调服务时带上请求头（操作人）：跟客户端经 gRPC 调一样，服务、ActionFilter 都从请求头取操作人。各冒烟工程链接这一个文件
/// </summary>
static class SmokeCall
{
    /// <summary>
    /// 以 user 的名义调服务
    /// </summary>
    public static CallContext As(string user)
    {
        return new CallContext(new object(), Context(user, "/xyz.Shared.Services.SmokeService/Call"));
    }

    /// <summary>
    /// 假的服务端调用上下文：user 为 null 是没带操作人；方法名、来路由测试给
    /// </summary>
    public static ServerCallContext Context(string? user, string method, string peer = "ipv4:127.0.0.1:50000")
    {
        var headers = new Metadata();
        if (user is not null)
        {
            headers.Add(RpcHeaders.Operator, Encoding.UTF8.GetBytes(user));
        }

        return new SmokeServerCallContext(headers, method, peer);
    }

    private sealed class SmokeServerCallContext(Metadata headers, string method, string peer) : ServerCallContext
    {
        private readonly Metadata _trailers = new();

        protected override string MethodCore => method;

        protected override string HostCore => "localhost";

        protected override string PeerCore => peer;

        protected override DateTime DeadlineCore => DateTime.MaxValue;

        protected override Metadata RequestHeadersCore => headers;

        protected override CancellationToken CancellationTokenCore => CancellationToken.None;

        protected override Metadata ResponseTrailersCore => _trailers;

        protected override Status StatusCore { get; set; }

        protected override WriteOptions? WriteOptionsCore { get; set; }

        protected override AuthContext AuthContextCore => new(null, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        {
            throw new NotSupportedException("冒烟用不到");
        }

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            return Task.CompletedTask;
        }
    }
}
