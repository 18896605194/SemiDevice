using NLog;

namespace xyz.Common.Log;

/// <summary>
/// 基于 NLog 的统一日志入口，仅保留 Debug/Info/Warn/Error 四个等级。
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
        var item = new LogItem(string.Empty, LogLevel.Debug, message);
        Logger.Debug(item.ToString());
    }

    public static void Debug(string module, string message)
    {
        var item = new LogItem(module, LogLevel.Debug, message);
        Logger.Debug(item.ToString());
    }

    public static void Info(string message)
    {
        var item = new LogItem(string.Empty, LogLevel.Info, message);
        Logger.Info(item.ToString());
    }

    public static void Info(string module, string message)
    {
        var item = new LogItem(module, LogLevel.Info, message);
        Logger.Info(item.ToString());
    }

    public static void Warn(string message)
    {
        var item = new LogItem(string.Empty, LogLevel.Warn, message);
        Logger.Warn(item.ToString());
    }

    public static void Warn(string module, string message)
    {
        var item = new LogItem(module, LogLevel.Warn, message);
        Logger.Warn(item.ToString());
    }

    public static void Error(string message)
    {
        var item = new LogItem(string.Empty, LogLevel.Error, message);
        Logger.Error(item.ToString());
    }

    public static void Error(string module, string message)
    {
        var item = new LogItem(module, LogLevel.Error, message);
        Logger.Error(item.ToString());
    }

}
