namespace xyz.Client.DataCenter.Models;

/// <summary>
/// 级别/等级筛选下拉框的选项，第一项"全部"表示不筛。
/// </summary>
public static class LevelOptions
{
    public const string All = "全部";

    /// <summary>日志级别。</summary>
    public static IReadOnlyList<string> Log { get; } = [All, "Debug", "Info", "Warn", "Error"];

    /// <summary>报警等级。</summary>
    public static IReadOnlyList<string> Alarm { get; } = [All, "Warn", "Alarm1", "Alarm2", "Fatal"];

    /// <summary>
    /// 下拉框选的值 → 查询条件（"全部"为空串，表示不筛）。
    /// </summary>
    public static string ToQuery(string? level)
    {
        return string.IsNullOrEmpty(level) || level == All ? string.Empty : level;
    }
}
