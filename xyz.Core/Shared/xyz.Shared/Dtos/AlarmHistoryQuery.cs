using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 报警历史查询：按时间段查报警记录（报出、清除各一行），可再按等级、关键字筛；最多返回的条数按后端 sc.xml 配置（取最新的）。
/// 时间用本机时间，传过去只保留刻度。
/// </summary>
[ProtoContract]
public class AlarmHistoryQuery
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
    /// 只查这个等级（Warn / Alarm1 / Alarm2 / Fatal）；空表示全部。
    /// </summary>
    [ProtoMember(3)]
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// 来源、报警代码或报警文本里包含这段文字；空表示不筛。
    /// </summary>
    [ProtoMember(4)]
    public string Keyword { get; set; } = string.Empty;

    /// <summary>
    /// 最多返回条数，超出只留最新的；&lt;=0 按后端配置（sc.xml 的 Alarm 节点 HistoryQueryMaxCount），
    /// 大于 0 时取它和配置里较小的那个。
    /// </summary>
    [ProtoMember(5)]
    public int MaxCount { get; set; }
}
