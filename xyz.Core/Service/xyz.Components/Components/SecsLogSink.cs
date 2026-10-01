using xyz.Common.Log;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;

namespace xyz.Components.Components;

/// <summary>
/// xyz.Secs 的日志出口：协议库零日志依赖，这里把它的状态与报文明文转进 LogHelper
/// （跟 NLog、客户端日志下拉框同一条链）。报文明文压成单行（树形缩进换成 " | "），免得一条日志占几十行。
/// </summary>
internal sealed class SecsLogSink : ISecsSink
{
    private readonly string _category;
    private readonly bool _traceEnabled;

    public SecsLogSink(string category, bool traceEnabled)
    {
        _category = category;
        _traceEnabled = traceEnabled;
    }

    public void Info(string category, string message) => LogHelper.Info(_category, message);

    public void Warn(string category, string message) => LogHelper.Warn(_category, message);

    public void Error(string category, string message) => LogHelper.Error(_category, message);

    public void Trace(SecsMessageDirection direction, HsmsMessage message)
    {
        if (!_traceEnabled)
        {
            return;
        }

        var arrow = direction == SecsMessageDirection.Sent ? ">>" : "<<";
        LogHelper.Info(_category, $"{arrow} {SecsMessageText.Format(message).Replace("\r", " ").Replace("\n", " | ")}");
    }
}
