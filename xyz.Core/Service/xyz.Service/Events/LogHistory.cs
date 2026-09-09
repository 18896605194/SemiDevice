using System.Collections.Concurrent;
using xyz.Shared.Dtos;

namespace xyz.Service.Events;

/// <summary>
/// 后端日志环形缓冲：保留最近 N 条。
/// 实时日志是发生类消息（不重放），客户端晚连上时用 ILogService.GetRecentAsync 补这段历史。
/// </summary>
public static class LogHistory
{
    /// <summary>
    /// 缓冲条数，与客户端拉取条数保持一致。
    /// </summary>
    public const int Capacity = 200;

    private static readonly ConcurrentQueue<LogDto> Items = new();

    /// <summary>
    /// 记一条日志；超出容量丢最旧的。由日志队列的单消费者调用。
    /// </summary>
    public static void Add(LogDto log)
    {
        Items.Enqueue(log);

        while (Items.Count > Capacity && Items.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// 当前缓冲快照（时间升序）。
    /// </summary>
    public static IReadOnlyList<LogDto> Snapshot()
    {
        return Items.ToArray();
    }
}
