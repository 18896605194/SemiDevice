using xyz.Common.Log;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;

namespace xyz.Elements.Elements;

/// <summary>
/// xyz.Secs 的日志出口接到框架日志：链路状态、各标准的日志按级别记在所属元件名下（Eap 下面的 E30、Hsms 这些）；
/// 报文明文只在给了 trace 开关、开关为真时记成收发原文（链路元件的 LogMessages）
/// </summary>
internal sealed class SecsLog : ISecsSink
{
    private readonly ILogSource _source;
    private readonly Func<bool>? _traceEnabled;

    /// <summary>
    /// 不记报文明文的（各标准用）
    /// </summary>
    public SecsLog(ILogSource source)
    {
        _source = source;
    }

    /// <summary>
    /// 记报文明文的（链路用）：traceEnabled 每条报文现问
    /// </summary>
    public SecsLog(ILogSource source, Func<bool> traceEnabled)
    {
        _source = source;
        _traceEnabled = traceEnabled;
    }

    public void Info(string category, string message)
    {
        LogHelper.Info(_source, message);
    }

    public void Warn(string category, string message)
    {
        LogHelper.Warn(_source, message);
    }

    public void Error(string category, string message)
    {
        LogHelper.Error(_source, message);
    }

    public void Trace(SecsMessageDirection direction, HsmsMessage message)
    {
        if (_traceEnabled is null || !_traceEnabled())
        {
            return;
        }

        string text = SecsMessageText.Format(message).Replace("\r", " ").Replace("\n", " | ");
        if (direction == SecsMessageDirection.Sent)
        {
            LogHelper.Comm(_source, LogHelper.Sent, text);
            return;
        }

        LogHelper.Comm(_source, LogHelper.Received, text);
    }
}
