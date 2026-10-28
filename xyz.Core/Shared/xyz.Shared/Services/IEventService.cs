using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Tools;

namespace xyz.Shared.Services;

/// <summary>
/// 跨进程事件通道契约。
/// </summary>
[ServiceContract]
public interface IEventService
{
    [OperationContract]
    IAsyncEnumerable<EventMessage> SubscribeAsync(CallContext context = default);
}
