using xyz.Secs.Hsms;

namespace xyz.Secs.Diagnostics;

/// <summary>报文方向：发出 / 收到。</summary>
public enum SecsMessageDirection
{
    Sent = 0,
    Received = 1,
}

/// <summary>
/// 类库的日志出口：xyz.Secs 不依赖任何日志框架，宿主注入实现（转 NLog/控制台/报文窗口都行）。
/// Trace 是每条 HSMS 报文的明文（SecsMessageText 已格式化好），fab 验收和现场排障全靠它。
/// </summary>
public interface ISecsSink
{
    void Info(string category, string message);

    void Warn(string category, string message);

    void Error(string category, string message);

    void Trace(SecsMessageDirection direction, HsmsMessage message);
}

/// <summary>什么都不做的出口：宿主不注入时的默认值，协议库保持零依赖。</summary>
public sealed class NullSecsSink : ISecsSink
{
    public static readonly NullSecsSink Instance = new();

    public void Info(string category, string message)
    {
    }

    public void Warn(string category, string message)
    {
    }

    public void Error(string category, string message)
    {
    }

    public void Trace(SecsMessageDirection direction, HsmsMessage message)
    {
    }
}
