using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// Trace 数据采集（S2F23 / S6F1）：按 Host 指定的周期采 SV，攒够一包发给 Host
/// </summary>
[Component(description: "Trace 数据采集（S2F23 / S6F1）：按 Host 指定的周期采 SV 发给 Host")]
public class E30TraceComponent : ComponentBase, IE30TraceComponent
{
    /// <summary>
    /// TIAACK 0：收下
    /// </summary>
    private const byte TraceAccepted = 0;

    /// <summary>
    /// TIAACK 3：周期不对
    /// </summary>
    private const byte InvalidPeriod = 3;

    /// <summary>
    /// TIAACK 4：有 SVID 不认识（E5 里 3 以上归设备自己定，4 是各家通行的用法）
    /// </summary>
    private const byte UnknownSvid = 4;

    /// <summary>
    /// TIAACK 5：几次一包（REPGSZ）不对，设备自己定的
    /// </summary>
    private const byte InvalidGroupSize = 5;

    private readonly object _gate = new();
    private readonly Dictionary<string, GemTrace> _traces = [];
    private IE30Component? _gem;

    /// <summary>
    /// GEM 编号表（接链路时取）：认 SVID、采 SV 都查它
    /// </summary>
    private GemCollectors? _collectors;

    /// <summary>
    /// 接到链路和 GEM 上（EAP 组件在链路打开之前调）：登记 S2F23，订离线
    /// </summary>
    public void Attach(IHsms link, IE30Component gem)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        _gem = gem;
        _collectors = IocHelper.GetService<GemCollectors>();
        gem.WentOffline += OnWentOffline;
        link.Handle(2, 23, TraceInitialize);
    }

    /// <summary>
    /// 从 GEM 上摘下来，停掉全部 Trace（宿主退出）
    /// </summary>
    public void Detach()
    {
        var gem = _gem;
        if (gem is not null)
        {
            gem.WentOffline -= OnWentOffline;
        }

        StopAll("EAP 退出");
        _gem = null;
    }

    /// <summary>
    /// S2F23 → S2F24：全部查完只回一次，TOTSMP=0 取消，同号替换
    /// </summary>
    private SecsReply TraceInitialize(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S2F23", 5);
        var traceId = body[0];
        string key = traceId.ToString();
        uint totalSamples = SecsRead.Id(body[2], "TOTSMP");
        if (totalSamples == 0)
        {
            Stop(key, "Host 取消");
            return Ack(TraceAccepted);
        }

        uint groupSize = SecsRead.Id(body[3], "REPGSZ");
        var svids = SecsRead.Ids(body[4], "SVID");

        var period = Period(body[1]);
        if (period is null)
        {
            LogHelper.Warn(Name, $"S2F23 Trace {key} 周期不对：{body[1]}");
            return Ack(InvalidPeriod);
        }

        if (groupSize == 0)
        {
            LogHelper.Warn(Name, $"S2F23 Trace {key} 几次一包（REPGSZ）是 0");
            return Ack(InvalidGroupSize);
        }

        var unknown = svids.Where(svid => !IsTraceable(svid)).ToList();
        if (svids.Count == 0 || unknown.Count > 0)
        {
            LogHelper.Warn(Name, $"S2F23 Trace {key} 没给 SVID 或有不认识的：{string.Join("、", unknown)}");
            return Ack(UnknownSvid);
        }

        var gem = _gem;
        var collectors = _collectors;
        if (gem is null || collectors is null)
        {
            return SecsReply.Abort;
        }

        var trace = new GemTrace(traceId, period.Value, totalSamples, groupSize, svids, collectors, gem, OnFinished, Name);
        GemTrace? replaced;
        lock (_gate)
        {
            _traces.TryGetValue(key, out replaced);
            _traces[key] = trace;
        }

        if (replaced is not null)
        {
            replaced.Stop();
            LogHelper.Info(Name, $"Trace {key} 换成新的，旧的停了");
        }

        LogHelper.Info(Name, $"Trace {key} 开始：每 {period.Value} 采一次，共 {totalSamples} 次，{groupSize} 次一包，SVID {string.Join("、", svids)}");
        return Ack(TraceAccepted).Then(trace.Start);
    }

    /// <summary>
    /// DSPER：6 位 hhmmss 或 8 位 hhmmsscc（厘秒），分、秒不过 59，不能是 0；不对返回 null
    /// </summary>
    private static TimeSpan? Period(SecsItem item)
    {
        if (item.Format != SecsFormat.Ascii)
        {
            return null;
        }

        string text = item.GetString();
        if ((text.Length != 6 && text.Length != 8) || !text.All(char.IsAsciiDigit))
        {
            return null;
        }

        int hours = int.Parse(text[..2], CultureInfo.InvariantCulture);
        int minutes = int.Parse(text[2..4], CultureInfo.InvariantCulture);
        int seconds = int.Parse(text[4..6], CultureInfo.InvariantCulture);
        int centiseconds = 0;
        if (text.Length == 8)
        {
            centiseconds = int.Parse(text[6..8], CultureInfo.InvariantCulture);
        }

        if (minutes > 59 || seconds > 59)
        {
            return null;
        }

        var period = new TimeSpan(0, hours, minutes, seconds, centiseconds * 10);
        if (period <= TimeSpan.Zero)
        {
            return null;
        }

        return period;
    }

    /// <summary>
    /// 这个 SVID 在用、标了上传（跟 S1F3 认的一样）
    /// </summary>
    private bool IsTraceable(uint svid)
    {
        var collectors = _collectors;
        return collectors is not null && svid <= int.MaxValue && collectors.Sv.TryRead((int)svid, out var row, out _) && row is not null && row.Visible;
    }

    private static SecsReply Ack(byte code)
    {
        return SecsReply.Of(SecsItem.B(code));
    }

    private void Stop(string key, string reason)
    {
        GemTrace? trace;
        lock (_gate)
        {
            _traces.Remove(key, out trace);
        }

        if (trace is not null)
        {
            trace.Stop();
            LogHelper.Info(Name, $"Trace {key} 停了：{reason}");
        }
    }

    private void StopAll(string reason)
    {
        List<GemTrace> traces;
        lock (_gate)
        {
            traces = _traces.Values.ToList();
            _traces.Clear();
        }

        foreach (var trace in traces)
        {
            trace.Stop();
        }

        if (traces.Count > 0)
        {
            LogHelper.Info(Name, $"{reason}：停掉 {traces.Count} 个 Trace");
        }
    }

    private void OnWentOffline()
    {
        StopAll("离线了");
    }

    /// <summary>
    /// 一个任务结束了：还是表里那个才删（被替换掉的旧任务不删新的）
    /// </summary>
    private void OnFinished(GemTrace trace)
    {
        string key = trace.TraceId.ToString();
        lock (_gate)
        {
            if (_traces.TryGetValue(key, out var current) && ReferenceEquals(current, trace))
            {
                _traces.Remove(key);
            }
        }
    }
}
