using System.Collections.Concurrent;
using xyz.Components.Components;
using xyz.Shared.Dtos;

namespace xyz.Service.Events;

/// <summary>
/// 后端日志环形缓冲：保留最近 N 条（N 在 sc.xml 的 Log 节点 RecentLogCount 配）。
/// 实时日志是发生类消息（不重放），客户端晚连上时用 ILogService.GetRecentAsync 补这段历史。
/// </summary>
public static class LogHistory
{
    /// <summary>
    /// 缓冲条数：sc.xml Log 节点的 RecentLogCount，没配或没装日志组件时用默认值。
    /// </summary>
    public static int Capacity
    {
        get
        {
            var configured = LogComponent.Current?.RecentLogCount ?? 0;
            return configured > 0 ? configured : 200;
        }
    }

    private static readonly ConcurrentQueue<LogDto> Items = new();

    /// <summary>
    /// 记一条日志；超出容量丢最旧的。由日志队列的单消费者调用。
    /// </summary>
    public static void Add(LogDto log)
    {
        Items.Enqueue(log);

        var capacity = Capacity;
        while (Items.Count > capacity && Items.TryDequeue(out _))
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
