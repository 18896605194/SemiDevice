using NLog;

namespace xyz.Common.Log;

public class LogItem
{
    /// <summary>
    /// 产生时刻（本地时间）
    /// </summary>
    public DateTime Time { get; set; } = DateTime.Now;

    /// <summary>
    /// 第一层：模块（元件树上最顶上那个元件的全路径），不是元件的写 Host、Operation 这类固定名字
    /// </summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// 第二层：模块下面的部件（Serial、RFID.Serial）；模块自己写的为空
    /// </summary>
    public string Part { get; set; } = string.Empty;

    public LogLevel Level { get; set; } = LogLevel.Info;

    /// <summary>
    /// 收发原文的方向（发 / 收），写在级别那一格；普通日志为空
    /// </summary>
    public string Direction { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public LogItem()
    {
    }

    public LogItem(string module, string part, LogLevel level, string message)
    {
        Time = DateTime.Now;
        Module = module;
        Part = part;
        Level = level;
        Message = message;
    }

    /// <summary>
    /// 级别那一格显示的字：收发原文写方向，其余写级别
    /// </summary>
    public string LevelText
    {
        get
        {
            if (Direction.Length > 0)
            {
                return Direction;
            }

            return Level.ToString().ToUpperInvariant();
        }
    }

    /// <summary>
    /// 写进文件的一行：【时间】 【级别】 【模块】 【部件】 内容（没有部件就不写那一格）
    /// </summary>
    public override string ToString()
    {
        if (Part.Length == 0)
        {
            return $"【{Time:yyyy-MM-dd HH:mm:ss.fff}】 【{LevelText}】 【{Module}】 {Message}";
        }

        return $"【{Time:yyyy-MM-dd HH:mm:ss.fff}】 【{LevelText}】 【{Module}】 【{Part}】 {Message}";
    }
}
