using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ProtoBuf.Grpc;
using xyz.Common.Events;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Events;

/// <summary>
/// 事件通道
/// </summary>
public class EventService : BaseService, IEventService
{
    public IAsyncEnumerable<EventMessage> SubscribeAsync(CallContext context = default)
    {
        var channel = Channel.CreateUnbounded<EventMessage>(new UnboundedChannelOptions { SingleReader = true });
      
        channel.Writer.TryWrite(new EventMessage());  //发一条空的消息

        var subscription = RemoteEventPublisher.SubscribeSent(message => channel.Writer.TryWrite(message));  //
        foreach (var retained in RemoteEventPublisher.GetRetained())  //获取黑板上的内容，发送一下
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
