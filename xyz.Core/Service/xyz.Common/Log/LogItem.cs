using NLog;

namespace xyz.Common.Log;

public class LogItem
{
    public string Module { get; set; } = string.Empty;

    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Message { get; set; } = string.Empty;

    public LogItem()
    {
    }

    public LogItem(string module, LogLevel level, string message)
    {
        Module = module;
        Level = level;
        Message = message;
    }

    public override string ToString()
    {
        var level = Level.ToString().ToUpperInvariant();
        return $"【{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}】 【{level}】 【{Module}】 {Message}";
    }
}
