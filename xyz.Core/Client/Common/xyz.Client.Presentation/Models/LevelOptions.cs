using xyz.Client.Presentation.Localization;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 级别/等级筛选下拉框的选项，第一项"全部"表示不筛（文字按当前界面语言）。级别本身是代码，不翻译。
/// </summary>
public static class LevelOptions
{
    /// <summary>"全部"。</summary>
    public static string All => L10n.Get("common.all");

    /// <summary>日志级别。</summary>
    public static IReadOnlyList<string> Log => [All, "Debug", "Info", "Warn", "Error"];

    /// <summary>报警等级。</summary>
    public static IReadOnlyList<string> Alarm => [All, "Warn", "Alarm1", "Alarm2", "Fatal"];

    /// <summary>
    /// 下拉框选的值 → 查询条件（"全部"为空串，表示不筛）。
    /// </summary>
    public static string ToQuery(string? level)
    {
        return string.IsNullOrEmpty(level) || level == All ? string.Empty : level;
    }
}
