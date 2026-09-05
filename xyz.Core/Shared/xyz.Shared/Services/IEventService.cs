using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Shared.Services;

/// <summary>
/// 跨进程事件通道契约。
/// 客户端 SubscribeAsync 建立服务端流接收后端事件（连接即重放留存消息）；
/// 需要上行时用 PublishAsync（进入后端进程内总线，不广播回其他客户端）。
/// </summary>
[ServiceContract]
public interface IEventService
{
    /// <summary>
    /// 订阅后端事件流（服务端流式；连接建立时先重放各键最后一条留存消息）。
    /// </summary>
    [OperationContract]
    IAsyncEnumerable<EventMessage> SubscribeAsync(EventSubscription request, CallContext context = default);

    /// <summary>
    /// 客户端 → 后端上行发布。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> PublishAsync(EventMessage message, CallContext context = default);
}
