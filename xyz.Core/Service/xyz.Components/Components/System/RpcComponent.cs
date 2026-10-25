using xyz.Components.Attributes;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 对前端壳的 gRPC 服务端点（machine.xml 的 Rpc 节点）：监听地址与端口。
/// 宿主在装配完组件树之后从这里取值配 Kestrel；machine.xml 没配该节点时退回默认值 localhost:5000（与老版本一致）。
/// 端口规划注意：HSMS（EAP 链路）惯例端口也是 5000，接 EAP 时把其中一边挪开。
/// </summary>
[Component(description: "对前端壳的 gRPC 服务端点（监听地址与端口）")]
public class RpcComponent : ComponentBase, IRpc
{

    public const int DefaultPort = 5000;

    public const int DefaultMaxMessageSizeMb = 26;

    /// <summary>
    /// 监听地址：前端壳和后端同机，保持 localhost；远程调试时才需要改。
    /// </summary>
    [Machine("localhost", "Rpc", "gRPC 监听地址：前端壳与本服务同机，保持 localhost；改端口要同步改客户端 exe 旁的 client.json")]
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// 监听端口：默认 5000（与老版本一致）。HSMS 惯例端口也是 5000，接 EAP 时这里挪走，
    /// 客户端 exe 旁的 client.json（GrpcAddress）跟着改，两边要一致。
    /// </summary>
    [Machine("5000", "Rpc", "gRPC 监听端口，默认 5000。注意 HSMS（EAP）惯例端口也是 5000，接 EAP 时把一边挪开；改这里要同步改客户端的 client.json")]
    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// 单条消息上限：gRPC 默认 4 MB，放到 26 MB 给大查询留余量
    /// </summary>
    [Machine("26", "Rpc", "单条 gRPC 消息最大多少 MB（后端收的上限，gRPC 默认是 4 MB）；客户端收的上限在客户端 exe 旁 client.json 的 MaxMessageSizeMb，两边保持一致")]
    public int MaxMessageSizeMb { get; set; } = DefaultMaxMessageSizeMb;
}
