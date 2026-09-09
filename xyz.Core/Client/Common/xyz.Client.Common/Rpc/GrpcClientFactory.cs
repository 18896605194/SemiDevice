using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;

namespace xyz.Client.Common.Rpc;

/// <summary>
/// 通用 gRPC 客户端工厂。
/// Channel 由程序启动时初始化一次，后续所有服务代理共用。
/// </summary>
public static class GrpcClientFactory
{
    private const string DefaultAddress = "http://localhost:5000";

    private static GrpcChannel? _channel;

    /// <summary>
    /// 在程序启动时调用一次，创建全局唯一的 GrpcChannel。
    /// </summary>
    public static void Initialize(string address = DefaultAddress)
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        _channel = GrpcChannel.ForAddress(address);
    }

    public static T Create<T>() where T : class
    {
        return GetChannel().CreateGrpcService<T>();
    }

    private static GrpcChannel GetChannel()
    {
        return _channel
            ?? throw new InvalidOperationException("请先在程序启动时调用 GrpcClientFactory.Initialize()");
    }
}
