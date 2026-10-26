using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Shared.Services;

/// <summary>
/// 跨进程事件通道契约。
/// </summary>
[ServiceContract]
public interface IEventService
{
    [OperationContract]
    IAsyncEnumerable<EventMessage> SubscribeAsync(EventSubscription request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> PublishAsync(EventMessage message, CallContext context = default);
}
