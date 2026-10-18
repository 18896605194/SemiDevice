using xyz.Common.Log;
using xyz.Components.Collectors;
using xyz.Components.Interfaces;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// 一个 Trace 任务：按周期读 SV，攒够一包发一条 S6F1，采够总数就结束
/// </summary>
internal sealed class GemTrace
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly TimeSpan _period;
    private readonly uint _totalSamples;
    private readonly uint _groupSize;
    private readonly IReadOnlyList<uint> _svids;
    private readonly GemCollectors _collectors;
    private readonly IE30Component _gem;
    private readonly Action<GemTrace> _finished;
    private readonly string _owner;

    /// <param name="traceId">Host 给的 TRID。</param>
    /// <param name="period">采样周期。</param>
    /// <param name="totalSamples">一共采几次（TOTSMP）。</param>
    /// <param name="groupSize">几次一包（REPGSZ）。</param>
    /// <param name="svids">采哪些 SV，按 Host 给的先后。</param>
    /// <param name="collectors">GEM 编号表：按 SVID 读 SV。</param>
    /// <param name="gem">S6F1 经它发。</param>
    /// <param name="finished">结束了（采完、停下、出错）。</param>
    /// <param name="owner">日志里写谁。</param>
    public GemTrace(SecsItem traceId, TimeSpan period, uint totalSamples, uint groupSize, IReadOnlyList<uint> svids,
        GemCollectors collectors, IE30Component gem, Action<GemTrace> finished, string owner)
    {
        TraceId = traceId;
        _period = period;
        _totalSamples = totalSamples;
        _groupSize = groupSize;
        _svids = svids;
        _collectors = collectors;
        _gem = gem;
        _finished = finished;
        _owner = owner;
    }

    /// <summary>
    /// Host 给的 TRID，S6F1 原样带回
    /// </summary>
    public SecsItem TraceId { get; }

    /// <summary>
    /// 开始采（S2F24 回出去以后），第一次在一个周期以后
    /// </summary>
    public void Start()
    {
        _ = Task.Run(RunAsync);
    }

    /// <summary>
    /// 停下，没攒满的那包不发
    /// </summary>
    public void Stop()
    {
        _cancel.Cancel();
    }

    private async Task RunAsync()
    {
        var values = new List<SecsItem>();
        uint sampled = 0;
        try
        {
            using var timer = new PeriodicTimer(_period);
            while (sampled < _totalSamples && await timer.WaitForNextTickAsync(_cancel.Token).ConfigureAwait(false))
            {
                sampled++;
                var time = DateTime.Now;
                values.AddRange(Sample());
                if (sampled % _groupSize == 0 || sampled == _totalSamples)
                {
                    // E5：SMPLN、STIME 记这一包里最后一次采样
                    var body = SecsItem.L(TraceId, SecsItem.U4(sampled), SecsItem.A(GemValue.Time(time, _gem.TimeFormat)), SecsItem.L(values));
                    _gem.Send(new SecsMessage(6, 1, true, body));
                    values = [];
                }
            }

            LogHelper.Info(_owner, $"Trace {TraceId} 采完了，共 {sampled} 次");
        }
        catch (OperationCanceledException)
        {
            // Host 取消、替换，或者离线了，停下的地方已记日志
        }
        catch (Exception exception)
        {
            LogHelper.Error(_owner, $"Trace {TraceId} 采样出错，停了：{exception.Message}");
        }
        finally
        {
            _finished(this);
        }
    }

    /// <summary>
    /// 按 Host 给的先后读一次 SV，读不到的给空 ASCII 占位
    /// </summary>
    private List<SecsItem> Sample()
    {
        var items = new List<SecsItem>();
        foreach (uint svid in _svids)
        {
            if (_collectors.Sv.TryRead((int)svid, out var row, out object? raw) && row is not null && row.Visible)
            {
                items.Add(GemValue.From(raw, row.Format));
            }
            else
            {
                items.Add(SecsItem.A(string.Empty));
            }
        }

        return items;
    }
}
