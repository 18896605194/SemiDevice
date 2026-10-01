using System.Text.Json;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using xyz.Client.Common.Log;

namespace xyz.Client.Common.Rpc;

/// <summary>
/// 通用 gRPC 客户端工厂。
/// Channel 由程序启动时初始化一次，后续所有服务代理共用。
/// 地址优先级：显式传参 &gt; client.json 的 GrpcAddress（跟客户端 exe 同目录，可选）&gt; 默认 localhost:5000。
/// 端口必须在连上后端之前就知道——后端的端口配在 sc.xml 的 Rpc 节点里，客户端没法走 gRPC 去问
/// （鸡生蛋），所以客户端这边的覆盖走本地文件，和后端 sc.xml 保持一致即可。
/// </summary>
public static class GrpcClientFactory
{
    private const string DefaultAddress = "http://localhost:5000";

    private static GrpcChannel? _channel;

    /// <summary>
    /// 在程序启动时调用一次，创建全局唯一的 GrpcChannel。
    /// </summary>
    public static void Initialize(string? address = null)
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        address ??= ReadClientConfig() ?? DefaultAddress;
        _channel = GrpcChannel.ForAddress(address);
        ClientLog.Info("Client", $"gRPC 通道：{address}");
    }

    public static T Create<T>() where T : class
    {
        return GetChannel().CreateGrpcService<T>();
    }

    /// <summary>
    /// 读 exe 旁的 client.json（可缺）里的 GrpcAddress，例如 { "GrpcAddress": "http://localhost:5050" }。
    /// 文件不存在、格式不对、字段缺失都按 null 处理（用默认地址连），不让配置问题挡启动。
    /// </summary>
    private static string? ReadClientConfig()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "client.json");
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("GrpcAddress", out var value) ? value.GetString() : null;
        }
        catch (Exception exception)
        {
            ClientLog.Warn("Client", $"client.json 读取失败，按默认地址 {DefaultAddress} 连：{exception.Message}");
            return null;
        }
    }

    private static GrpcChannel GetChannel()
    {
        return _channel
            ?? throw new InvalidOperationException("请先在程序启动时调用 GrpcClientFactory.Initialize()");
    }
}
