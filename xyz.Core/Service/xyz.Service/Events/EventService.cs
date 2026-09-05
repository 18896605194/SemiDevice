using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ProtoBuf.Grpc;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Events;

/// <summary>
/// 事件通道 gRPC 插座：把后端进程内的静态 EventBus 桥接到客户端。
/// 每个客户端连接 = 一条服务端流，连接建立即重放留存消息，断开即退订。
/// </summary>
public class EventService : IEventService
{
    /// <inheritdoc/>
    public IAsyncEnumerable<EventMessage> SubscribeAsync(EventSubscription request, CallContext context = default)
    {
        var channel = Channel.CreateUnbounded<EventMessage>(
            new UnboundedChannelOptions { SingleReader = true });

        var subscription = EventBus.SubscribeRaw(message => channel.Writer.TryWrite(message));
        foreach (var retained in EventBus.GetRetained())
        {
            channel.Writer.TryWrite(retained);
        }

        context.CancellationToken.Register(() =>
        {
            subscription.Dispose();
            channel.Writer.TryComplete();
        });

        return ReadStream(channel.Reader, context.CancellationToken);
    }

    /// <inheritdoc/>
    public Task<RpcResponse> PublishAsync(EventMessage message, CallContext context = default)
    {
        // 上行：投递进后端进程内总线（本地订阅者可收到，不回声、不进入留存）
        EventBus.Deliver(message);
        return Task.FromResult(RpcResponse.Ok("{}"));
    }

    private static async IAsyncEnumerable<EventMessage> ReadStream(
        ChannelReader<EventMessage> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var message))
            {
                yield return message;
            }
        }
    }
}
