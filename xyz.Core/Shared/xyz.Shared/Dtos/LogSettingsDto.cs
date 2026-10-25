namespace xyz.Shared.Dtos;

/// <summary>
/// 日志显示设置（后端 machine.xml 的 Log 节点），客户端连上后端时拉一次；没拉到之前用这里的默认值。
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

    /// <summary>
    /// 日志文件留多少天（客户端自己的日志也照这个）；0 = 不删
    /// </summary>
    public int KeepDays { get; set; }

    /// <summary>
    /// 日志历史页"模块"下拉的选项：日志的第一层（组件树最顶上那层，按 machine.xml 先后，再加 Operation、Host）
    /// </summary>
    public List<string> Modules { get; set; } = [];
}
