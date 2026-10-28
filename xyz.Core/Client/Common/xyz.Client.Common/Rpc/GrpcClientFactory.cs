using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using xyz.Client.Common.Log;
using xyz.Client.Common.Session;
using xyz.Shared.Rpc;
using xyz.Tools;

namespace xyz.Client.Common.Rpc;

/// <summary>
///  客户端工厂
/// </summary>
public static class GrpcClientFactory
{
    private const string DefaultAddress = "http://localhost:5000";

    private const int DefaultMaxMessageSizeMb = 16;


    /// <summary>
    /// 通道外面包一层 UserHeader，服务代理都从它建
    /// </summary>
    private static CallInvoker? _invoker;

    public static void Initialize(string? address = null)
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var config = ReadClientConfig();  //读出服务端的基础配置就是地址
        address ??= config.GrpcAddress ?? DefaultAddress;
        int maxMessageSizeMb = DefaultMaxMessageSizeMb;
        if (config.MaxMessageSizeMb > 0)
        {
            maxMessageSizeMb = config.MaxMessageSizeMb;
        }

        //创建通道，记录地址 
        var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = maxMessageSizeMb * 1024 * 1024,
        });
        _invoker = channel.Intercept(new UserHeader());  //创建通道增加外壳
        ClientLog.Info("Client", $"gRPC 通道：{address}，单条消息上限 {maxMessageSizeMb} MB");
    }

    public static T Create<T>() where T : class
    {
        return GetInvoker().CreateGrpcService<T>();
    }

    /// <summary>
    /// 读 exe 旁的 client.json（可缺），例如 { "GrpcAddress": "http://localhost:5050", "MaxMessageSizeMb": 26 }。
    /// 文件不存在、格式不对、字段缺失都按默认值处理，不让配置问题挡启动。
    /// </summary>
    private static ClientConfig ReadClientConfig()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "client.json");
            if (!File.Exists(path))
            {
                return new ClientConfig();
            }

            return JsonHelper.Deserialize<ClientConfig>(File.ReadAllText(path)) ?? new ClientConfig();
        }
        catch (Exception exception)
        {
            ClientLog.Warn("Client", $"client.json 读取失败，按默认地址 {DefaultAddress}、默认上限 {DefaultMaxMessageSizeMb} MB 连：{exception.Message}");
            return new ClientConfig();
        }
    }

    /// <summary>
    /// client.json 的内容
    /// </summary>
    private sealed class ClientConfig
    {
        public string? GrpcAddress { get; set; }

        public int MaxMessageSizeMb { get; set; }
    }

    private static CallInvoker GetInvoker()
    {
        return _invoker?? throw new InvalidOperationException("请先在程序启动时调用 GrpcClientFactory.Initialize()");
    }

    /// <summary>
    /// 每次调用都在请求头里带上当前操作人（ClientSession.UserName：登录名，没登录是 Guest，没启用登录是 Admin）
    /// </summary>
    private sealed class UserHeader : Interceptor
    {
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
            BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            return continuation(request, WithOperator(context));
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            return continuation(request, WithOperator(context));
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context, AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            return continuation(request, WithOperator(context));
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context, AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            return continuation(WithOperator(context));
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context, AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            return continuation(WithOperator(context));
        }

        /// <summary>
        /// 原来的请求头照抄一份，再加上操作人（UTF-8 字节：登录名可能是中文）
        /// </summary>
        private static ClientInterceptorContext<TRequest, TResponse> WithOperator<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
            where TRequest : class
            where TResponse : class
        {
            var headers = new Metadata();
            var original = context.Options.Headers;
            if (original is not null)
            {
                foreach (var entry in original)
                {
                    headers.Add(entry);
                }
            }

            headers.Add(RpcHeaders.Operator, Encoding.UTF8.GetBytes(ClientSession.UserName));
            return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, context.Options.WithHeaders(headers));
        }
    }
}
