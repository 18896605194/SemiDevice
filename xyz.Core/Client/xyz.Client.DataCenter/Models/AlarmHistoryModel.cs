namespace xyz.Client.DataCenter.Models;

/// <summary>
/// 报警历史页的一行，由 AlarmHistoryDto 经 Mapster 映射，字段与 DTO 同名对齐。
/// </summary>
public class AlarmHistoryModel
{
    /// <summary>变化：Raised（报出）/ Cleared（人工清除）。</summary>
    public string Action { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    /// <summary>等级：Warn / Alarm1 / Alarm2 / Fatal。</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>这次报警报出的时刻。</summary>
    public DateTime RaisedAt { get; set; }

    /// <summary>这条记录发生的时刻（报出或清除）。</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>动作文字。</summary>
    public string ActionText => Action == "Cleared" ? "清除" : "报出";

    /// <summary>报了多久才被清除（只有清除那条有）。</summary>
    public string DurationText
    {
        get
        {
            if (Action != "Cleared")
            {
                return string.Empty;
            }

            var duration = OccurredAt - RaisedAt;
            return duration.TotalDays >= 1
                ? $"{(int)duration.TotalDays}天 {duration:hh\\:mm\\:ss}"
                : duration.ToString("hh\\:mm\\:ss");
        }
    }
}
