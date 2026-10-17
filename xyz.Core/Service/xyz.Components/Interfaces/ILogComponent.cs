using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 日志：设置，加上转给界面的最近日志
/// </summary>
public interface ILogComponent
{
    /// <summary>
    /// 日志历史一次最多查几条
    /// </summary>
    int HistoryQueryMaxCount { get; }

    /// <summary>
    /// 客户端连上时补发最近多少条
    /// </summary>
    int RecentLogCount { get; }

    /// <summary>
    /// 实时日志页最多显示几条
    /// </summary>
    int RealtimeDisplayMaxCount { get; }

    /// <summary>
    /// 底部日志栏最多显示几条
    /// </summary>
    int LogBarDisplayMaxCount { get; }

    /// <summary>
    /// 最近一段后端日志（最旧的在前），客户端连上时补拉
    /// </summary>
    IReadOnlyList<LogDto> Recent();
}
