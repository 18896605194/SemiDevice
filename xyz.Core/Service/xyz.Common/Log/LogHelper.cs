using System.Globalization;
using System.Text;
using NLog;
using NLog.Targets;

namespace xyz.Common.Log;

/// <summary>
/// 后端统一的日志入口（后端记日志一律走它）：主日志 Log\xyz-日期.log、轮询文件 Log\poll-日期.log，NLog 异步写；
/// Warn 以上的运行日志另进 <see cref="LogQueue"/>，由日志组件推给界面。每行带两层名字：模块、部件（见 <see cref="ILogSource"/>）
/// </summary>
public static class LogHelper
{
    /// <summary>
    /// 宿主自己（开机、退出、框架报错）的第一层
    /// </summary>
    public const string HostModule = "Host";

    /// <summary>
    /// 操作日志（谁点了什么）的第一层
    /// </summary>
    public const string OperationModule = "Operation";

    /// <summary>
    /// 收发原文的方向：发出去的
    /// </summary>
    public const string Sent = "发";

    /// <summary>
    /// 收发原文的方向：收到的
    /// </summary>
    public const string Received = "收";

    /// <summary>
    /// 写主日志的 logger（NLog.config 里按名字分文件）
    /// </summary>
    private static readonly Logger MainLogger;

    /// <summary>
    /// 写轮询文件的 logger
    /// </summary>
    private static readonly Logger LoopLogger;

    static LogHelper()
    {
        var configFile = Path.Combine(AppContext.BaseDirectory, "Log", "NLog.config");
        if (File.Exists(configFile))
        {
            LogManager.Setup().LoadConfigurationFromFile(configFile);
        }

        MainLogger = LogManager.GetLogger("Main");
        LoopLogger = LogManager.GetLogger("Loop");
    }

    #region 运行日志：组件写的带两层名字，不是组件的写固定名字（Host 这类）

    public static void Debug(ILogSource source, string message)
    {
        Write(source.LogModule, source.LogPart, LogLevel.Debug, message);
    }

    public static void Debug(string module, string message)
    {
        Write(module, string.Empty, LogLevel.Debug, message);
    }

    public static void Info(ILogSource source, string message)
    {
        Write(source.LogModule, source.LogPart, LogLevel.Info, message);
    }

    public static void Info(string module, string message)
    {
        Write(module, string.Empty, LogLevel.Info, message);
    }

    public static void Warn(ILogSource source, string message)
    {
        Write(source.LogModule, source.LogPart, LogLevel.Warn, message);
    }

    public static void Warn(string module, string message)
    {
        Write(module, string.Empty, LogLevel.Warn, message);
    }

    public static void Error(ILogSource source, string message)
    {
        Write(source.LogModule, source.LogPart, LogLevel.Error, message);
    }

    public static void Error(string module, string message)
    {
        Write(module, string.Empty, LogLevel.Error, message);
    }

    #endregion

    #region 操作日志、收发原文：只落文件，不推界面

    /// <summary>
    /// 操作日志（谁点了什么）：进主日志，第一层是 Operation；不推顶栏（界面自己会提示结果）
    /// </summary>
    public static void Operation(LogLevel level, string message)
    {
        var item = new LogItem(OperationModule, string.Empty, level, message);
        MainLogger.Log(level, item.ToString());
    }

    /// <summary>
    /// 收发原文（动作指令、设备主动发来的、解不出来的）：进主日志，级别那一格写方向
    /// </summary>
    public static void Comm(ILogSource source, string direction, string frame)
    {
        var item = Frame(source, direction, frame);
        MainLogger.Info(item.ToString());
    }

    /// <summary>
    /// 状态轮询的收发原文：进轮询文件，不进主日志
    /// </summary>
    public static void InfoLoop(ILogSource source, string direction, string frame)
    {
        var item = Frame(source, direction, frame);
        LoopLogger.Info(item.ToString());
    }

    #endregion

    #region 保留天数、收尾

    /// <summary>
    /// 日志文件留多少天（日志组件开机时按 machine.xml 的 KeepDays 设）：每个文件目标按天清掉更早的
    /// </summary>
    public static void SetKeepDays(int days)
    {
        var configuration = LogManager.Configuration;
        if (configuration is null || days <= 0)
        {
            return;
        }

        foreach (var target in configuration.AllTargets.OfType<FileTarget>())
        {
            target.MaxArchiveDays = days;
        }

        // 配置重新挂一遍：开着的文件关掉重开。NLog 只在打开文件时按天数清旧文件，不这样要等到换天才清
        LogManager.Configuration = configuration;
    }

    /// <summary>
    /// 把队列里还没写的日志写完（崩溃前用），最多等这么久
    /// </summary>
    public static void Flush(TimeSpan timeout)
    {
        LogManager.Flush(timeout);
    }

    /// <summary>
    /// 退出时调：队列写完、文件关掉
    /// </summary>
    public static void Shutdown()
    {
        LogManager.Shutdown();
    }

    #endregion

    /// <summary>
    /// 控制字符写成 &lt;CR&gt;、&lt;LF&gt;、&lt;0x02&gt;，一帧原文在日志里占一行；能显示的（包括中文）原样
    /// </summary>
    public static string Escape(string frame)
    {
        var text = new StringBuilder(frame.Length);
        foreach (char c in frame)
        {
            if (c == '\r')
            {
                text.Append("<CR>");
            }
            else if (c == '\n')
            {
                text.Append("<LF>");
            }
            else if (char.IsControl(c))
            {
                text.Append("<0x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture)).Append('>');
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static LogItem Frame(ILogSource source, string direction, string frame)
    {
        return new LogItem(source.LogModule, source.LogPart, LogLevel.Info, Escape(frame))
        {
            Direction = direction,
        };
    }

    private static void Write(string module, string part, LogLevel level, string message)
    {
        var item = new LogItem(module, part, level, message);
        MainLogger.Log(level, item.ToString());
        LogQueue.Enqueue(item);
    }
}
