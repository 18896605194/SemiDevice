using System.Reactive.Linq;
using System.Reactive.Subjects;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.DataCharts;

namespace xyz.Components.Components;

/// <summary>
/// 实时曲线：专管往界面推。自己不采样——订阅数据曲线（DataChartComponent）的每周期一行，
/// 内存里留最近一段（WindowSeconds），每来一行经 <see cref="Frames"/> 交给推送桥（xyz.Service 的 RealChartPublisher）发给界面；
/// 实时曲线页打开、新勾一条曲线时先从 <see cref="Recent"/> 把这一段补上，不用等曲线从零长起来。
/// </summary>
[Component(description: "实时曲线（订阅数据曲线的采样，留最近一段，推给界面）")]
public class RealChartComponent : ComponentBase
{
    public static RealChartComponent? Current { get; set; }

    public RealChartComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("600", "RealChart", "内存里留最近多少秒：实时曲线页打开、新勾曲线时先补这一段，也是页面上能选的最长显示范围")]
    public int WindowSeconds { get; set; } = 600;

    #endregion

    private readonly object _gate = new();

    private readonly Queue<DataRecord> _recent = new();

    private readonly Subject<DataRecord> _frames = new();

    private DataChartComponent? _source;

    private IDisposable? _subscription;

    /// <summary>
    /// 这一轮采样的标识（接上采样的时刻，UTC 毫秒）：每帧都带着，界面对不上就知道后端重启过、信号表得重新拉。
    /// </summary>
    public long Session { get; private set; }

    /// <summary>
    /// 推的是哪些信号，顺序即每帧值的顺序（就是数据曲线的采样信号表）。
    /// </summary>
    public IReadOnlyList<DataSignal> Signals => _source?.Signals ?? [];

    public int SampleIntervalMs => _source?.SampleIntervalMs ?? 1000;

    /// <summary>
    /// 每来一行发一帧，在采样线程上发出；推送桥要 ObserveOn 到自己的线程，推慢了不拖采样。
    /// </summary>
    public IObservable<DataRecord> Frames => _frames.AsObservable();

    /// <summary>
    /// 接到数据曲线的采样流上（装配完、开始采样之前调）。
    /// </summary>
    public void Attach(DataChartComponent source)
    {
        _subscription?.Dispose();
        _source = source;
        Session = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _subscription = source.Records.Subscribe(Keep, _frames.OnCompleted);
        LogHelper.Info(Name, $"实时曲线接上数据曲线的采样，内存留最近 {WindowSeconds} 秒");
    }

    /// <summary>
    /// 最近一段（最旧的在前）。
    /// </summary>
    public IReadOnlyList<DataRecord> Recent()
    {
        lock (_gate)
        {
            return _recent.ToArray();
        }
    }

    private void Keep(DataRecord record)
    {
        long oldest = record.Time - Math.Max(1, WindowSeconds) * 1000L;
        lock (_gate)
        {
            _recent.Enqueue(record);
            while (_recent.Count > 0 && _recent.Peek().Time <= oldest)
            {
                _recent.Dequeue();
            }
        }

        _frames.OnNext(record);
    }
}
