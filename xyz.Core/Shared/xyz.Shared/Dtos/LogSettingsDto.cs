namespace xyz.Shared.Dtos;

/// <summary>
/// 日志显示设置（后端 sc.xml 的 Log 节点），客户端连上后端时拉一次；没拉到之前用这里的默认值。
/// </summary>
public class LogSettingsDto
{
    /// <summary>
    /// 实时日志页最多显示条数的默认值。
    /// </summary>
    public const int DefaultRealtimeDisplayMaxCount = 2000;

    /// <summary>
    /// 顶部日志栏最多保留条数的默认值。
    /// </summary>
    public const int DefaultLogBarDisplayMaxCount = 500;

    /// <summary>
    /// 实时日志页最多显示多少条，超出丢最旧的。
    /// </summary>
    public int RealtimeDisplayMaxCount { get; set; } = DefaultRealtimeDisplayMaxCount;

    /// <summary>
    /// 顶部日志栏最多保留多少条，超出丢最旧的。
    /// </summary>
    public int LogBarDisplayMaxCount { get; set; } = DefaultLogBarDisplayMaxCount;
}
