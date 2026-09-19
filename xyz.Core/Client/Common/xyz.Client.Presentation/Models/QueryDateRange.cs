namespace xyz.Client.DataCenter.Models;

/// <summary>
/// 历史查询的时间段：界面选两个日期（含首尾两天），查询按 [起始日 0 点, 截止日次日 0 点)；选反了自动对调。
/// </summary>
public readonly record struct QueryDateRange(DateTime Start, DateTime End)
{
    public static QueryDateRange Of(DateTime first, DateTime last)
    {
        var from = first <= last ? first : last;
        var to = first <= last ? last : first;
        return new QueryDateRange(from.Date, to.Date.AddDays(1));
    }
}
