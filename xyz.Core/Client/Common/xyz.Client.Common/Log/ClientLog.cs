using System.Threading.Channels;
using xyz.Shared.Dtos;

namespace xyz.Client.Common.Log;

/// <summary>
/// 客户端日志队列：所有来源（后端事件流、客户端自身报错）都先入队，
/// 界面作为单消费者不停从队列取，取到一条显示一条；生产者不直接碰界面。
/// 队列有界，满了丢最旧的，避免日志刷屏拖垮界面。
/// </summary>
public static class ClientLog
{
    private const int Capacity = 2000;

    private static readonly Channel<LogDto> _channel = Channel.CreateBounded<LogDto>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>
    /// 消费端：界面（单消费者）用 await foreach 从队列取，不需要轮询。
    /// </summary>
    public static ChannelReader<LogDto> Reader => _channel.Reader;

    /// <summary>
    /// 入队一条日志；队列满时丢弃最旧的一条。线程安全、非阻塞。
    /// </summary>
    public static void Enqueue(LogDto log)
    {
        if (log is null)
        {
            return;
        }

        _channel.Writer.TryWrite(log);
    }

    public static void Error(string module, string message)
    {
        Enqueue(new LogDto
        {
            Level = "Error",
            Module = module,
            Message = message,
            Source = "Client",
        });
    }

    public static void Warn(string module, string message)
    {
        Enqueue(new LogDto
        {
            Level = "Warn",
            Module = module,
            Message = message,
            Source = "Client",
        });
    }

    public static void Info(string module, string message)
    {
        Enqueue(new LogDto
        {
            Level = "Info",
            Module = module,
            Message = message,
            Source = "Client",
        });
    }
}
