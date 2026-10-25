using Microsoft.Extensions.Logging;
using xyz.Common.Log;

namespace xyz.Components.Logging;

/// <summary>
/// 把第三方库（ASP.NET Core 宿主、OPC UA）自己的日志转进 LogHelper：后端日志只有 LogHelper 一条路；
/// 只转 Warn 以上，它们的 Info 太碎
/// </summary>
public sealed class LogHelperLoggerProvider : ILoggerProvider
{
    private readonly ILogSource _source;

    /// <summary>
    /// 记在某个组件名下（OPC UA 记在 PLC 组件下）
    /// </summary>
    public LogHelperLoggerProvider(ILogSource source)
    {
        _source = source;
    }

    /// <summary>
    /// 记在一个固定的第一层名下（宿主记 Host）
    /// </summary>
    public LogHelperLoggerProvider(string module)
    {
        _source = new FixedSource(module);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new LogHelperLogger(_source, categoryName);
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// 只有第一层、没有部件的来源
    /// </summary>
    private sealed class FixedSource(string module) : ILogSource
    {
        public string LogModule => module;

        public string LogPart => string.Empty;
    }

    private sealed class LogHelperLogger(ILogSource source, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Warning && logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = $"{category}：{formatter(state, exception)}";
            if (exception is not null)
            {
                message = $"{message}（{exception.Message}）";
            }

            if (logLevel == LogLevel.Warning)
            {
                LogHelper.Warn(source, message);
                return;
            }

            LogHelper.Error(source, message);
        }
    }
}
