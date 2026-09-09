using System.Threading.Channels;
using NLog;

namespace xyz.Common.Log;

/// <summary>
/// 日志队列
/// </summary>
public static class LogQueue
{
    public static LogLevel MinLevel { get; set; } = LogLevel.Warn;

    private const int Capacity = 2000;

    private static readonly Channel<LogItem> _channel = Channel.CreateBounded<LogItem>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private static readonly object Gate = new();
    private static Task? _consumer;

    public static void Enqueue(LogItem item)
    {
        if (item is null || item.Level < MinLevel)
        {
            return;
        }

        _channel.Writer.TryWrite(item);
    }

    /// <summary>
    /// 启动单消费者：一条一条取出交给 sink。只生效一次（重复调用忽略）。
    /// 消费者 Task 保存在 <see cref="_consumer"/>，不丢弃，便于观察与收尾。
    /// </summary>
    public static void Start(Action<LogItem> sink)
    {
        lock (Gate)
        {
            _consumer ??= ConsumeAsync(sink);
        }
    }

    private static async Task ConsumeAsync(Action<LogItem> sink)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            try
            {
                sink(item);
            }
            catch
            {

            }
        }
    }

    /// <summary>
    /// 停止入队并结束消费者循环（进程退出/测试收尾用）。
    /// </summary>
    public static void Stop()
    {
        _channel.Writer.TryComplete();
    }
}
