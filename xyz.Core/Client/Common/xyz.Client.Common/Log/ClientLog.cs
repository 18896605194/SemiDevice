using System.IO;
using System.Threading.Channels;
using NLog;
using xyz.Shared.Dtos;

namespace xyz.Client.Common.Log;

/// <summary>
/// 客户端日志队列：所有来源（后端事件流、客户端自身报错）都先入队，
/// 界面作为单消费者不停从队列取，取到一条显示一条；生产者不直接碰界面。
/// 队列有界，满了丢最旧的，避免日志刷屏拖垮界面。
/// 每条日志同时交给 NLog 落文件（exe 同目录 Log\client-yyyy-MM-dd.log），
/// 界面还没起来或打不开时也能查到原因；配置见 Log\NLog.config。
/// </summary>
public static class ClientLog
{
    private const int Capacity = 2000;

    private static readonly Logger Logger;

    private static readonly Channel<LogDto> _channel = Channel.CreateBounded<LogDto>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    static ClientLog()
    {
        try
        {
            // 与后端 LogHelper 一致：配置文件在 exe 同目录 Log\NLog.config。
            var configFile = Path.Combine(AppContext.BaseDirectory, "Log", "NLog.config");
            if (File.Exists(configFile))
            {
                LogManager.Setup().LoadConfigurationFromFile(configFile);
            }
        }
        catch
        {
            // 配置有问题时退化成"只进界面队列"，不影响业务。
        }

        Logger = LogManager.GetLogger(nameof(ClientLog));
    }

    /// <summary>
    /// 消费端：界面（单消费者）用 await foreach 从队列取，不需要轮询。
    /// </summary>
    public static ChannelReader<LogDto> Reader => _channel.Reader;

    /// <summary>
    /// 入队一条日志：进界面队列 + 交 NLog 落文件。队列满时丢弃最旧的一条。
    /// 线程安全、非阻塞。后端事件流日志和客户端自身日志都走这里。
    /// </summary>
    public static void Enqueue(LogDto log)
    {
        if (log is null)
        {
            return;
        }

        _channel.Writer.TryWrite(log);
        Logger.Log(ToNLogLevel(log.Level), "{0}: {1}", log.Module, log.Message);
    }

    public static void Error(string module, string message)
    {
        Write("Error", module, message);
    }

    public static void Warn(string module, string message)
    {
        Write("Warn", module, message);
    }

    public static void Info(string module, string message)
    {
        Write("Info", module, message);
    }

    /// <summary>
    /// 客户端自身日志：统一走 Enqueue，避免落文件写两遍。
    /// </summary>
    private static void Write(string level, string module, string message)
    {
        Enqueue(new LogDto
        {
            Level = level,
            Module = module,
            Message = message,
            Source = "Client",
        });
    }

    private static LogLevel ToNLogLevel(string? level)
    {
        return level switch
        {
            "Error" => LogLevel.Error,
            "Warn" => LogLevel.Warn,
            "Debug" => LogLevel.Debug,
            _ => LogLevel.Info,
        };
    }
}
