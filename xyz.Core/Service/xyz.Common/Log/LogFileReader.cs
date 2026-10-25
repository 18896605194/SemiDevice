using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NLog;

namespace xyz.Common.Log;

/// <summary>
/// 读后端主日志文件，给日志历史查询用，只读不写。
/// 文件与格式都跟着写入方走：NLog.config 的 file 目标（运行目录 Log\xyz-yyyy-MM-dd.log，超 100MB 再分出 .1、.2……），
/// 每条日志一行，格式即 <see cref="LogItem.ToString"/>（部件那一格没有就不写，改之前的旧文件也没有）；消息里带换行（如异常堆栈）时续行归到上一条。
/// 轮询文件 poll-yyyy-MM-dd.log 不读（量大，只在机台上开文件看）。
/// </summary>
public static class LogFileReader
{
    /// <summary>
    /// 日志目录：运行目录下的 Log（与 NLog.config 一致）。
    /// </summary>
    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "Log");

    private const string FilePrefix = "xyz-";

    private static readonly Regex LinePattern = new(
        @"^【(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})】 【(?<level>[^】]+)】 【(?<module>[^】]*)】(?: 【(?<part>[^】]*)】)? ?(?<message>.*)$",
        RegexOptions.Compiled);

    /// <summary>
    /// 按天找文件、按行序读出 [start, end) 之间的日志。文件正被 NLog 写着也能读。
    /// </summary>
    public static IEnumerable<LogItem> Read(DateTime start, DateTime end)
    {
        for (var day = start.Date; day < end; day = day.AddDays(1))
        {
            foreach (var path in FilesOf(day))
            {
                foreach (var item in ReadFile(path))
                {
                    if (item.Time >= start && item.Time < end)
                    {
                        yield return item;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 解析一行日志；不是一条日志的开头（续行、空行）返回 false。
    /// </summary>
    public static bool TryParse(string line, out LogItem item)
    {
        item = new LogItem();
        var match = LinePattern.Match(line);
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        // 收发原文的级别那一格写的是方向
        string levelText = match.Groups["level"].Value;
        string direction = string.Empty;
        LogLevel level = LogLevel.Info;
        if (levelText == LogHelper.Sent || levelText == LogHelper.Received)
        {
            direction = levelText;
        }
        else
        {
            try
            {
                level = LogLevel.FromString(levelText);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        item = new LogItem
        {
            Time = time,
            Level = level,
            Direction = direction,
            Module = match.Groups["module"].Value,
            Part = match.Groups["part"].Value,
            Message = match.Groups["message"].Value,
        };
        return true;
    }

    /// <summary>
    /// 一天的文件：先按序号读分出去的旧文件（.1、.2……），最后读当天正在写的那个。
    /// </summary>
    private static IEnumerable<string> FilesOf(DateTime day)
    {
        if (!Directory.Exists(LogDirectory))
        {
            return [];
        }

        var stem = FilePrefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Directory.EnumerateFiles(LogDirectory, stem + "*.log")
            .Select(path => (Path: path, Order: OrderOf(Path.GetFileNameWithoutExtension(path), stem)))
            .Where(file => file.Order is not null)
            .OrderBy(file => file.Order)
            .Select(file => file.Path);
    }

    /// <summary>
    /// 当天正在写的文件排最后（int.MaxValue），分出去的按序号排；名字对不上的（别的日期前缀）返回 null。
    /// </summary>
    private static int? OrderOf(string name, string stem)
    {
        if (name.Length == stem.Length)
        {
            return int.MaxValue;
        }

        return name[stem.Length] == '.' && int.TryParse(name.AsSpan(stem.Length + 1), out var number) ? number : null;
    }

    private static IEnumerable<LogItem> ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        LogItem? current = null;
        StringBuilder? continued = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (TryParse(line, out var next))
            {
                if (current is not null)
                {
                    yield return Complete(current, continued);
                }

                current = next;
                continued = null;
            }
            else if (current is not null && line.Length > 0)
            {
                continued ??= new StringBuilder(current.Message);
                continued.Append('\n').Append(line);
            }
        }

        if (current is not null)
        {
            yield return Complete(current, continued);
        }
    }

    private static LogItem Complete(LogItem item, StringBuilder? continued)
    {
        if (continued is not null)
        {
            item.Message = continued.ToString();
        }

        return item;
    }
}
