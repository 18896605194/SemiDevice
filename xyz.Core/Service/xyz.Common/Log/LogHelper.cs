using NLog;

namespace xyz.Common.Log;

/// <summary>
/// 基于 NLog 的统一日志入口，仅保留 Debug/Info/Warn/Error 四个等级。
/// 每条日志落 NLog（控制台 + 文件）的同时进 <see cref="LogQueue"/>，
/// 由单消费者转发到 EventBus，最终显示在客户端日志下拉框。
/// </summary>
public static class LogHelper
{
    private static readonly Logger Logger;

    static LogHelper()
    {
        var configFile = Path.Combine(AppContext.BaseDirectory, "Log", "NLog.config");
        if (File.Exists(configFile))
        {
            LogManager.Setup().LoadConfigurationFromFile(configFile);
        }

        Logger = LogManager.GetCurrentClassLogger();
    }

    public static void Debug(string message)
    {
        Debug(string.Empty, message);
    }

    public static void Debug(string module, string message)
    {
        var item = new LogItem(module, LogLevel.Debug, message);
        Logger.Debug(item.ToString());
        LogQueue.Enqueue(item);
    }

    public static void Info(string message)
    {
        Info(string.Empty, message);
    }

    public static void Info(string module, string message)
    {
        var item = new LogItem(module, LogLevel.Info, message);
        Logger.Info(item.ToString());
        LogQueue.Enqueue(item);
    }

    public static void Warn(string message)
    {
        Warn(string.Empty, message);
    }

    public static void Warn(string module, string message)
    {
        var item = new LogItem(module, LogLevel.Warn, message);
        Logger.Warn(item.ToString());
        LogQueue.Enqueue(item);
    }

    public static void Error(string message)
    {
        Error(string.Empty, message);
    }

    public static void Error(string module, string message)
    {
        var item = new LogItem(module, LogLevel.Error, message);
        Logger.Error(item.ToString());
        LogQueue.Enqueue(item);
    }
}
