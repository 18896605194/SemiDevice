namespace xyz.Client.Presentation.Models;

/// <summary>
/// 历史查询的时间段：界面选两个时刻（精确到分钟，DateTimePicker），查询按 [起始那一分钟, 截止那一分钟的下一分钟)——
/// 截止那一分钟整分钟都算进去，选 23:59 就查到当天最后一秒；选反了自动对调。
/// </summary>
public readonly record struct QueryDateRange(DateTime Start, DateTime End)
{
    public static QueryDateRange Of(DateTime first, DateTime last)
    {
        var from = first <= last ? first : last;
        var to = first <= last ? last : first;
        return new QueryDateRange(ToMinute(from), ToMinute(to).AddMinutes(1));
    }

    /// <summary>
    /// 界面上"今天"：0:00 ~ 23:59。
    /// </summary>
    public static (DateTime Start, DateTime End) Today()
    {
        return LastDays(1);
    }

    /// <summary>
    /// 界面上"最近 N 天"（含今天）：N-1 天前的 0:00 ~ 今天 23:59。
    /// </summary>
    public static (DateTime Start, DateTime End) LastDays(int days)
    {
        return (DateTime.Today.AddDays(1 - days), DateTime.Today.AddDays(1).AddMinutes(-1));
    }

    /// <summary>
    /// 界面上"最近 N 小时"：N 小时前那一分钟 ~ 现在这一分钟。
    /// </summary>
    public static (DateTime Start, DateTime End) LastHours(int hours)
    {
        var now = ToMinute(DateTime.Now);
        return (now.AddHours(-hours), now);
    }

    private static DateTime ToMinute(DateTime time)
    {
        return new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
    }
}
