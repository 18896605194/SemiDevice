namespace xyz.Common;

/// <summary>
/// 时间换算：本机时间和 UTC 毫秒之间来回转（后端用）
/// </summary>
public static class DateTimeHelper
{
    /// <summary>
    /// 本机时间转 UTC 毫秒，不看 Kind
    /// </summary>
    public static long ToUnixMilliseconds(DateTime localTime)
    {
        // 界面传来的时间走 gRPC 只留刻度、Kind 丢了，所以一律当本机时间认
        return new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Local)).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// UTC 毫秒转本机时间
    /// </summary>
    public static DateTime FromUnixMilliseconds(long milliseconds)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;
    }

    /// <summary>
    /// 现在的 UTC 毫秒
    /// </summary>
    public static long NowUnixMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 带时区的时间转本机时间，空的还是空
    /// </summary>
    public static DateTime? LocalTimeOf(DateTimeOffset? time)
    {
        if (time is null)
        {
            return null;
        }

        return time.Value.LocalDateTime;
    }
}
