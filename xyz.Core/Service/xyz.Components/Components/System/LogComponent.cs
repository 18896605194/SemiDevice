using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 日志设置（sc.xml 的 Log 节点）：日志历史查询、最近日志缓冲、客户端日志显示条数这类全系统一份的日志配置。
/// </summary>
[Component(description: "日志设置（历史查询条数、显示条数等）")]
public class LogComponent : ComponentBase
{

    public static LogComponent? Current { get; set; }

    public LogComponent()
    {
        Current = this;
    }

    [SCEditor("1000", "Log", "日志历史页一次最多返回多少条（取最新的），超出界面提示缩小时间段或加条件")]
    public int HistoryQueryMaxCount { get; set; } = 1000;

    [SCEditor("200", "Log", "后端保留最近多少条日志，客户端连上（或重连）时补拉这一段")]
    public int RecentLogCount { get; set; } = 200;

    [SCEditor("2000", "Log", "客户端实时日志页最多显示多少条，超出丢最旧的")]
    public int RealtimeDisplayMaxCount { get; set; } = 2000;

    [SCEditor("500", "Log", "客户端顶部日志栏最多保留多少条，超出丢最旧的")]
    public int LogBarDisplayMaxCount { get; set; } = 500;
}
