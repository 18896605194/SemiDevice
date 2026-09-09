using NLog;

namespace xyz.Common.Log;

public class LogItem
{
    /// <summary>产生时刻（本地时间）。</summary>
    public DateTime Time { get; set; } = DateTime.Now;

    public string Module { get; set; } = string.Empty;

    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Message { get; set; } = string.Empty;

    public LogItem()
    {
    }

    public LogItem(string module, LogLevel level, string message)
    {
        Time = DateTime.Now;
        Module = module;
        Level = level;
        Message = message;
    }

    public override string ToString()
    {
        var level = Level.ToString().ToUpperInvariant();
        return $"【{Time:yyyy-MM-dd HH:mm:ss.fff}】 【{level}】 【{Module}】 {Message}";
    }
}
