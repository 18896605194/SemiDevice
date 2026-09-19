using SqlSugar;

namespace xyz.Database.Alarms;

/// <summary>
/// 报警记录：报警每报出一次、每被人工清除一次各写一行，用来回溯设备出过什么事、多久才处理。
/// 当前有哪些报警看报警组件本身，这张表只管历史。
/// 按天分表：实际表名是 alarm_history_20260918 这种，按 OccurredAt 分流；
/// 过期清理直接删整张表，不用逐行删。
/// </summary>
[SugarTable("alarm_history_{year}{month}{day}")]
[SplitTable(SplitType.Day)]
public class AlarmHistoryEntity
{
    /// <summary>
    /// 主键：雪花号（分表不能用自增列），本身按时间递增，排序即时间序。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; } = SnowFlakeSingle.Instance.NextId();

    /// <summary>变化：Raised（报出）/ Cleared（人工清除）。</summary>
    [SugarColumn(Length = 16)]
    public string Action { get; set; } = string.Empty;

    /// <summary>报警来源的组件路径，如 LoadPort1、Chamber1.Pressure。</summary>
    [SugarColumn(Length = 128)]
    public string Source { get; set; } = string.Empty;

    /// <summary>报警代码，同一来源内唯一。</summary>
    [SugarColumn(Length = 64)]
    public string Code { get; set; } = string.Empty;

    /// <summary>报警文本（记当时的，定义以后改了也不影响老记录）。</summary>
    [SugarColumn(Length = 128)]
    public string Text { get; set; } = string.Empty;

    /// <summary>分类（AlarmCategory 名字）。</summary>
    [SugarColumn(Length = 32)]
    public string Category { get; set; } = string.Empty;

    /// <summary>等级（AlarmLevel 名字）。</summary>
    [SugarColumn(Length = 16)]
    public string Level { get; set; } = string.Empty;

    /// <summary>这次报警报出的时刻；清除那行也带上，一行就能看出报了多久。</summary>
    public DateTime RaisedAt { get; set; }

    /// <summary>这次变化发生的时刻（报出行 = 报出时刻，清除行 = 清除时刻）；也是分表依据。</summary>
    [SplitField]
    public DateTime OccurredAt { get; set; }
}
