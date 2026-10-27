using xyz.Shared.Dtos;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 日志：设置，加上转给界面的最近日志
/// </summary>
public interface ILogElement
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
    /// 日志文件留多少天（后端主日志、轮询文件，客户端日志也照这个）
    /// </summary>
    int KeepDays { get; }

    /// <summary>
    /// 日志的第一层都有哪些（日志历史页模块下拉的选项）：元件树最顶上那层，按 machine.xml 先后，再加 Operation、Host
    /// </summary>
    IReadOnlyList<string> Modules { get; }

    /// <summary>
    /// 最近一段后端日志（最旧的在前），客户端连上时补拉
    /// </summary>
    IReadOnlyList<LogDto> Recent();
}
