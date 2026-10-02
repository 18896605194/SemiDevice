using xyz.Common.Log;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;

namespace xyz.Components.Components;

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
