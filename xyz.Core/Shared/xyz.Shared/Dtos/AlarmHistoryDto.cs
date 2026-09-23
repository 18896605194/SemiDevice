namespace xyz.Shared.Dtos;

/// <summary>
/// 一条报警记录：报警每报出一次、每被人工清除一次各一条。
/// </summary>
public class AlarmHistoryDto
{
    /// <summary>变化：Raised（报出）/ Cleared（人工清除）。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>报警来源的组件路径。</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>报警代码，同一来源内唯一。</summary>
    public string Code { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>分类（AlarmCategory 名字）。</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>等级（AlarmLevel 名字）。</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>这次报警报出的时刻；清除那条也带着，一看就知道报了多久。</summary>
    public DateTime RaisedAt { get; set; }

    /// <summary>这次变化发生的时刻（报出条 = 报出时刻，清除条 = 清除时刻）。</summary>
    public DateTime OccurredAt { get; set; }
}
