using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// 日志（sc.xml 的 Log 节点）：全系统一份的日志配置，加上把后端日志转给界面——留最近一段（客户端连上补拉）、每条实时推事件流。
/// </summary>
[Component(description: "日志（历史查询条数、显示条数，转给界面的实时日志和最近日志）")]
public class LogComponent : ComponentBase, ILogComponent
{
    private readonly object _gate = new();

    /// <summary>
    /// 最近的日志，最旧的在前
    /// </summary>
    private readonly Queue<LogDto> _recent = new();

    [SCEditor("1000", "Log", "日志历史页一次最多返回多少条（取最新的），超出界面提示缩小时间段或加条件")]
    public int HistoryQueryMaxCount { get; set; } = 1000;

    [SCEditor("200", "Log", "后端保留最近多少条日志，客户端连上（或重连）时补拉这一段；0 = 不留")]
    public int RecentLogCount { get; set; } = 200;

    [SCEditor("2000", "Log", "客户端实时日志页最多显示多少条，超出丢最旧的")]
    public int RealtimeDisplayMaxCount { get; set; } = 2000;

    [SCEditor("500", "Log", "客户端顶部日志栏最多保留多少条，超出丢最旧的")]
    public int LogBarDisplayMaxCount { get; set; } = 500;

    /// <summary>
    /// 开机：接上日志队列。开机到这会儿的日志都在队列里攒着，接上就补进来，一条不落
    /// </summary>
    public override void Bind()
    {
        LogQueue.Start(Forward);
    }

    /// <summary>
    /// 最近一段日志（最旧的在前）
    /// </summary>
    public IReadOnlyList<LogDto> Recent()
    {
        lock (_gate)
        {
            return _recent.ToArray();
        }
    }

    /// <summary>
    /// 日志队列的单消费者：留进最近一段，再推给界面（发生类消息，不留存）
    /// </summary>
    private void Forward(LogItem item)
    {
        var log = new LogDto
        {
            Time = item.Time,
            Level = item.Level.Name,
            Module = item.Module,
            Message = item.Message,
            Source = "Server",
        };

        lock (_gate)
        {
            _recent.Enqueue(log);
            while (_recent.Count > 0 && _recent.Count > RecentLogCount)
            {
                _recent.Dequeue();
            }
        }

        EventBus.Send(log, LogDto.EventToken, retain: false);
    }
}
