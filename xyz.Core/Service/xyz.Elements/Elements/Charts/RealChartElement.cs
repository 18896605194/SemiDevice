using xyz.Common;
using xyz.Common.Events;
using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Elements.Elements;

/// <summary>
/// 实时曲线：专管往界面推
/// </summary>
[Element(description: "实时曲线（订阅数据曲线的采样，留最近一段，推给界面）")]
public class RealChartElement : ElementBase, IRealChart
{
    #region Machine

    [Machine("3600", "RealChart", "内存里留最近多少秒：实时曲线页打开、新勾曲线时先补这一段，也是页面上能选的最长显示范围（页面有 1、2、5、10、30、60 分钟几档，超过这个的不给选）")]
    public int WindowSeconds { get; set; } = 3600;

    #endregion

    private readonly object _gate = new();

    private readonly Queue<DataRecord> _recent = new();

    /// <summary>
    /// 采到了、还没推出去的行
    /// </summary>
    private readonly Queue<DataRecord> _pending = new();

    private IDataChart? _source;

    /// <summary>
    /// 映射元件（开机 Bind 时取）：信号表转成给界面的样子
    /// </summary>
    private IMapper? _mapper;

    private IDisposable? _subscription;

    private string? _lastPushFault;

    /// <summary>
    /// 这一轮采样的标识（接上采样的时刻，UTC 毫秒）：每帧都带着，界面对不上就知道后端重启过、信号表得重新拉。
    /// </summary>
    public long Session { get; private set; }

    /// <summary>
    /// 推的是哪些信号，顺序即每帧值的顺序（就是数据曲线的采样信号表）。
    /// </summary>
    public IReadOnlyList<DataSignal> Signals
    {
        get
        {
            if (_source is null)
            {
                return [];
            }

            return _source.Signals;
        }
    }

    /// <summary>
    /// 采样周期（毫秒），跟数据曲线一样
    /// </summary>
    public int SampleIntervalMs
    {
        get
        {
            if (_source is null)
            {
                return 1000;
            }

            return _source.SampleIntervalMs;
        }
    }

    /// <summary>
    /// 最多同时画几条曲线，跟数据曲线同一个设置（没接上数据曲线时按数据曲线的默认值 20）
    /// </summary>
    public int MaxSignals
    {
        get
        {
            if (_source is null)
            {
                return 20;
            }

            return Math.Max(1, _source.QueryMaxSignals);
        }
    }

    /// <summary>
    /// 截图存到哪个文件夹，跟数据曲线同一个设置
    /// </summary>
    public string ScreenshotDirectory
    {
        get
        {
            if (_source is null)
            {
                return string.Empty;
            }

            return _source.ScreenshotDirectory;
        }
    }

    /// <summary>
    /// 开机（开始扫描之前）：取映射元件，接到数据曲线的采样上
    /// </summary>
    public override void Bind()
    {
        _mapper = IocHelper.GetService<IMapper>();
        var source = IocHelper.GetService<IDataChart>();
        if (source is null)
        {
            LogHelper.Warn(this, "machine.xml 没配 DataChart 节点：实时曲线没有数据");
            return;
        }

        Attach(source);
    }

    /// <summary>
    /// 开机推一次信号表（留存）：界面连上（含重连）就照它长勾选树、对帧
    /// </summary>
    public override void PublishOnStartup()
    {
        if (_mapper is null)
        {
            return;
        }

        RemoteEventPublisher.Send(_mapper.Map<RealChartLayoutDto>(this), RealChartLayoutDto.EventToken);
    }

    /// <summary>
    /// 接到数据曲线的采样流上
    /// </summary>
    public void Attach(IDataChart source)
    {
        _subscription?.Dispose();
        _source = source;
        Session = DateTimeHelper.NowUnixMilliseconds();
        _subscription = source.Records.Subscribe(Keep);
        if (!IsRun)
        {
            LogHelper.Warn(this, "machine.xml 的 RealChart 节点没配 IsRun=true：不推，实时曲线页没有新数据");
        }

        LogHelper.Info(this, $"实时曲线接上数据曲线的采样，内存留最近 {WindowSeconds} 秒");
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

    /// <summary>
    /// 数据曲线采到一行（在它的扫描线程上）：留进最近一段，排着等推
    /// </summary>
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

            // 不扫描就没人推，不攒着
            if (IsRun)
            {
                _pending.Enqueue(record);
            }
        }
    }

    /// <summary>
    /// 扫描里把新来的行一帧一帧推给界面：推在自己的线程上，客户端慢了也拖不到数据曲线采样
    /// </summary>
    protected override void OnScan()
    {
        while (true)
        {
            DataRecord record;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                record = _pending.Dequeue();
            }

            Push(record);
        }
    }

    /// <summary>
    /// 推一帧（不留存，只给在线的）；推失败同样的错只记一次
    /// </summary>
    private void Push(DataRecord record)
    {
        try
        {
            RemoteEventPublisher.Send(ToFrame(record), RealChartFrameDto.EventToken, retain: false);
            _lastPushFault = null;
        }
        catch (Exception exception)
        {
            if (_lastPushFault != exception.Message)
            {
                _lastPushFault = exception.Message;
                LogHelper.Warn(this, $"实时曲线推送失败: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// 采样行转成推给界面的一帧：值转单精度（曲线够用，包小一半）
    /// </summary>
    private RealChartFrameDto ToFrame(DataRecord record)
    {
        var values = new List<float?>(record.Values.Length);
        foreach (var value in record.Values)
        {
            if (value is null)
            {
                values.Add(null);
            }
            else
            {
                values.Add((float)value.Value);
            }
        }

        return new RealChartFrameDto { Session = Session, Time = record.Time, Values = values };
    }
}
