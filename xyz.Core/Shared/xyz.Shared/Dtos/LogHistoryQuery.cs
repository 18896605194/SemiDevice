using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 日志历史查询：按时间段查后端日志文件，可再按级别、关键字筛；最多返回的条数按后端 sc.xml 配置（取最新的）。
/// 时间用本机时间，传过去只保留刻度。
/// </summary>
[ProtoContract]
public class LogHistoryQuery
{
    /// <summary>
    /// 起始时刻（含）。
    /// </summary>
    [ProtoMember(1)]
    public DateTime Start { get; set; }

    /// <summary>
    /// 截止时刻（不含）。
    /// </summary>
    [ProtoMember(2)]
    public DateTime End { get; set; }

    /// <summary>
    /// 只查这个级别（Debug / Info / Warn / Error）；空表示全部。
    /// </summary>
    [ProtoMember(3)]
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// 模块名或日志内容里包含这段文字（不分大小写）；空表示不筛。
    /// </summary>
    [ProtoMember(4)]
    public string Keyword { get; set; } = string.Empty;

    /// <summary>
    /// 最多返回条数，超出只留最新的；&lt;=0 按后端配置（sc.xml 的 Log 节点 HistoryQueryMaxCount），
    /// 大于 0 时取它和配置里较小的那个。
    /// </summary>
    [ProtoMember(5)]
    public int MaxCount { get; set; }
}
