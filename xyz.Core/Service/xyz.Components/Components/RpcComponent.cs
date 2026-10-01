using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 对前端壳的 gRPC 服务端点（sc.xml 的 Rpc 节点）：监听地址与端口。
/// 宿主在装配完组件树之后从这里取值配 Kestrel；sc.xml 没配该节点时退回默认值 localhost:5000（与老版本一致）。
/// 端口规划注意：HSMS（EAP 链路）惯例端口也是 5000，接 EAP 时把其中一边挪开。
/// </summary>
[Component(description: "对前端壳的 gRPC 服务端点（监听地址与端口）")]
public class RpcComponent : ComponentBase
{
    /// <summary>
    /// 当前端点配置；sc.xml 没配 Rpc 节点时为 null，宿主按默认端口监听。
    /// </summary>
    public static RpcComponent? Current { get; set; }

    public RpcComponent()
    {
        Current = this;
    }

    /// <summary>
    /// 监听地址：前端壳和后端同机，保持 localhost；远程调试时才需要改。
    /// </summary>
    [SCEditor("localhost", "Rpc", "gRPC 监听地址：前端壳与本服务同机，保持 localhost；改端口要同步改客户端 exe 旁的 client.json")]
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// 监听端口：默认 5000（与老版本一致）。HSMS 惯例端口也是 5000，接 EAP 时这里挪走，
    /// 客户端 exe 旁的 client.json（GrpcAddress）跟着改，两边要一致。
    /// </summary>
    [SCEditor("5000", "Rpc", "gRPC 监听端口，默认 5000。注意 HSMS（EAP）惯例端口也是 5000，接 EAP 时把一边挪开；改这里要同步改客户端的 client.json")]
    public int Port { get; set; } = 5000;
}
