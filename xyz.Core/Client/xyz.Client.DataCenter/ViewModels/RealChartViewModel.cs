using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataCenter.Models;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.DataCenter.ViewModels;

/// <summary>
/// 实时曲线页 ViewModel：后端每个采样周期推一帧（全部信号的值），勾了哪些画哪些，横轴跟着最新时刻往前走。
/// 勾新信号时先向后端要最近一段补上（后端内存里留着），不用等曲线从零长起来。
/// 暂停只是不跟着走了（方便盯着某一段看），数据照收；继续就回到最新。
/// 右下角每条曲线一行：光标处的值（鼠标不在图上就是最新值），当前看到那一段的最小 / 最大 / 平均。
///
/// Rx：帧流（事件总线 → Observable）逐帧追加；统计请求（每帧、缩放、平移都会来）按 250ms 取样再算，
/// 光标快速划过时不会每动一下都把几条曲线整段扫一遍。
/// </summary>
public class RealChartViewModel : BaseViewModel
{
    /// <summary>
    /// 页面上能选的显示范围（分钟），超过后端内存里留的那段的不给选。
    /// </summary>
    private static readonly int[] WindowChoices = [1, 2, 5, 10, 30, 60];

    #region Column

    /// <summary>
    /// 信号勾选树（模块 → 部件 → 属性）。
    /// </summary>
    public ObservableCollection<SignalNodeModel> Signals { get; } = [];

    /// <summary>
    /// 勾上的曲线，按勾选先后：图和右下角信息表都绑它。
    /// </summary>
    public ObservableCollection<TrendSeries> Series { get; } = [];

    private IReadOnlyList<int> _windowOptions = [1, 2, 5, 10];

    public IReadOnlyList<int> WindowOptions
    {
        get => _windowOptions;
        private set => SetProperty(ref _windowOptions, value);
    }

    private int _selectedWindow = 5;

    /// <summary>
    /// 显示最近几分钟。
    /// </summary>
    public int SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            if (SetProperty(ref _selectedWindow, value) && FollowRange)
            {
                SlideRange();
            }
        }
    }

    private bool _followRange = true;

    /// <summary>
    /// 横轴跟着最新时刻走；图上缩放、平移时图控件把它置假（停在那儿看），双击图或点"继续"回到最新。
    /// </summary>
    public bool FollowRange
    {
        get => _followRange;
        set
        {
            if (SetProperty(ref _followRange, value))
            {
                OnPropertyChanged(nameof(FollowText));
                if (value)
                {
                    SlideRange();
                }

                UpdateStatus();
            }
        }
    }

    /// <summary>
    /// 暂停 / 继续按钮上的字。
    /// </summary>
    public string FollowText => L10n.Get(FollowRange ? "realchart.pause" : "realchart.resume");

    private DateTime? _rangeStart;

    /// <summary>
    /// 图的"整段"：最近 SelectedWindow 分钟，每来一帧往前挪。
    /// </summary>
    public DateTime? RangeStart
    {
        get => _rangeStart;
        private set => SetProperty(ref _rangeStart, value);
    }

    private DateTime? _rangeEnd;

    public DateTime? RangeEnd
    {
        get => _rangeEnd;
        private set => SetProperty(ref _rangeEnd, value);
    }

    private DateTime? _visibleStart;

    /// <summary>
    /// 图上当前看到的时间段（图控件回写），统计按它算。
    /// </summary>
    public DateTime? VisibleStart
    {
        get => _visibleStart;
        set
        {
            if (SetProperty(ref _visibleStart, value))
            {
                _statsRequests.OnNext(Unit.Default);
            }
        }
    }

    private DateTime? _visibleEnd;

    public DateTime? VisibleEnd
    {
        get => _visibleEnd;
        set
        {
            if (SetProperty(ref _visibleEnd, value))
            {
                _statsRequests.OnNext(Unit.Default);
            }
        }
    }

    private DateTime? _cursorTime;

    /// <summary>
    /// 鼠标在图上所指的时刻（图控件回写），移出图为 null。
    /// </summary>
    public DateTime? CursorTime
    {
        get => _cursorTime;
        set
        {
            if (SetProperty(ref _cursorTime, value))
            {
                UpdateCursorValues();
            }
        }
    }

    private string _searchText = string.Empty;

    /// <summary>
    /// 信号树搜索：全路径里包含就显示（不分大小写）。
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                SignalTree.Filter(Signals, _searchText);
            }
        }
    }

    private int _maxSignals = 20;

    /// <summary>
    /// 最多同时画几条（跟数据曲线同一个设置）。
    /// </summary>
    public int MaxSignals
    {
        get => _maxSignals;
        private set => SetProperty(ref _maxSignals, value);
    }

    private string _status = L10n.Get("realchart.waiting");

    /// <summary>
    /// 工具栏右侧的状态：等数据、实时刷新到几点、已暂停、提示。
    /// </summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    #endregion

    #region Command

    public IRelayCommand ToggleFollowCommand { get; }

    public IRelayCommand ClearCommand { get; }

    public IAsyncRelayCommand RefreshSignalsCommand { get; }

    #endregion

    #region Service

    private readonly IRealChartService _service;

    private readonly Dictionary<string, TrendSeries> _seriesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 信号名 → 帧里第几个值。
    /// </summary>
    private Dictionary<string, int> _indexByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 统计请求：每帧、缩放、平移都会来，取样后再算。
    /// </summary>
    private readonly Subject<Unit> _statsRequests = new();

    private long _session;

    /// <summary>
    /// 后端内存里留多少秒：曲线上比这更早的点丢掉。
    /// </summary>
    private int _keepSeconds = 600;

    private DateTime? _latest;

    private bool _loadingLayout;

    private IDisposable? _frames;

    private IDisposable? _stats;

    private IDisposable? _reconnect;

    #endregion

    public RealChartViewModel()
    {
        _service = GrpcClientFactory.Create<IRealChartService>();

        ToggleFollowCommand = new RelayCommand(DoToggleFollow);
        ClearCommand = new RelayCommand(DoClear);
        RefreshSignalsCommand = new AsyncRelayCommand(DoRefreshSignals);
    }

    /// <summary>
    /// 接帧流（事件总线已经把帧投到界面线程）、搭统计取样、拉信号表；事件流重连（后端可能重启过）时重拉信号表。
    /// </summary>
    public override void Init()
    {
        var ui = SynchronizationContext.Current;

        _frames?.Dispose();
        _frames = Observable
            .Create<RealChartFrameDto>(observer => EventBus.Register<RealChartFrameDto>(RealChartFrameDto.EventToken, observer.OnNext))
            .Subscribe(OnFrame);

        IObservable<Unit> stats = _statsRequests.Sample(TimeSpan.FromMilliseconds(250));
        if (ui is not null)
        {
            stats = stats.ObserveOn(ui);
        }

        _stats?.Dispose();
        _stats = stats.Subscribe(_ => UpdateStats());

        _reconnect?.Dispose();
        _reconnect = Observable
            .FromEvent<bool>(handler => RemoteEventBus.ConnectionChanged += handler,
                handler => RemoteEventBus.ConnectionChanged -= handler)
            .Where(connected => connected)
            .Subscribe(connected => _ = LoadLayoutAsync());

        _ = LoadLayoutAsync();
    }

    private void DoToggleFollow()
    {
        FollowRange = !FollowRange;
    }

    private void DoClear()
    {
        foreach (var leaf in SignalTree.AllLeaves(Signals))
        {
            leaf.SetChecked(false);
        }

        SignalTree.RefreshGroups(Signals);
        Series.Clear();
        _seriesByName.Clear();
    }

    private Task DoRefreshSignals()
    {
        return LoadLayoutAsync();
    }

    /// <summary>
    /// 拉信号表（每帧值的顺序）、重长勾选树；还在的信号照样勾着，并把它们最近一段重新补上（后端重启过的话之前的数据接不上）。
    /// </summary>
    private async Task LoadLayoutAsync()
    {
        if (_loadingLayout)
        {
            return;
        }

        _loadingLayout = true;
        try
        {
            var response = await _service.GetLayoutAsync(new RpcRequest());
            if (!response.Success)
            {
                Status = L10n.Get("chart.load_failed", L10n.Get(response.Code, response.Args));
                return;
            }

            var layout = response.DeserializeData<RealChartLayoutDto>();
            _session = layout.Session;
            _keepSeconds = Math.Max(60, layout.WindowSeconds);
            MaxSignals = Math.Max(1, layout.MaxSignals);
            _indexByName = layout.Signals
                .Select((signal, index) => (signal.Name, Index: index))
                .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

            WindowOptions = WindowChoices.Where(minutes => minutes * 60 <= _keepSeconds).DefaultIfEmpty(1).ToList();
            if (!WindowOptions.Contains(SelectedWindow))
            {
                SelectedWindow = WindowOptions.Last();
            }

            // 信号表里没有了的曲线拿掉。
            foreach (var gone in Series.Where(series => !_indexByName.ContainsKey(series.Name)).ToList())
            {
                Series.Remove(gone);
                _seriesByName.Remove(gone.Name);
            }

            Signals.Clear();
            foreach (var root in SignalTree.Build(layout.Signals, OnToggle))
            {
                Signals.Add(root);
            }

            foreach (var leaf in SignalTree.AllLeaves(Signals))
            {
                leaf.SetChecked(_seriesByName.ContainsKey(leaf.Path));
            }

            SignalTree.RefreshGroups(Signals);
            SignalTree.Filter(Signals, SearchText);

            await FillRecentAsync(Series.Select(series => series.Name).ToList());
        }
        catch (Exception exception)
        {
            Status = L10n.Get("chart.load_failed", exception.Message);
            ClientLog.Warn("DataCenter", $"实时曲线信号表没拿到：{exception.Message}");
        }
        finally
        {
            _loadingLayout = false;
        }
    }

    /// <summary>
    /// 勾选树上点了一下：勾模块、部件就是勾下面全部（搜索时只动看得见的）；超过最多条数整次不勾，给提示。
    /// 新勾的先补最近一段。
    /// </summary>
    private void OnToggle(SignalNodeModel node, bool check)
    {
        var leaves = node.Leaves().Where(leaf => leaf.IsShown).ToList();
        if (check)
        {
            var adding = leaves.Where(leaf => leaf.IsChecked != true).ToList();
            if (Series.Count + adding.Count > MaxSignals)
            {
                Status = L10n.Get("chart.too_many", MaxSignals);
                return;
            }

            foreach (var leaf in adding)
            {
                leaf.SetChecked(true);
                var signal = leaf.Signal!;
                var series = new TrendSeries(signal.Name, signal.Unit, signal.IsDigital, signal.Description);
                _seriesByName[signal.Name] = series;
                Series.Add(series);
            }

            _ = FillRecentAsync(adding.Select(leaf => leaf.Path).ToList());
        }
        else
        {
            foreach (var leaf in leaves.Where(leaf => leaf.IsChecked == true))
            {
                leaf.SetChecked(false);
                if (_seriesByName.Remove(leaf.Path, out var series))
                {
                    Series.Remove(series);
                }
            }
        }

        SignalTree.RefreshGroups(Signals);
        _statsRequests.OnNext(Unit.Default);
    }

    /// <summary>
    /// 向后端要这几条曲线最近一段，整段换上；之后的帧接着往后追加。
    /// </summary>
    private async Task FillRecentAsync(List<string> names)
    {
        if (names.Count == 0)
        {
            return;
        }

        try
        {
            var response = await _service.GetRecentAsync(new RealChartRecentQuery { Names = names });
            var recent = response.DeserializeData<RealChartRecentDto>();
            if (recent.Session != _session)
            {
                return;
            }

            var xs = recent.Times.Select(ToLocalOADate).ToArray();
            foreach (var item in recent.Series)
            {
                if (_seriesByName.TryGetValue(item.Name, out var series))
                {
                    series.SetData(xs, item.Values.Select(value => value is { } number ? number : double.NaN));
                }
            }

            UpdateCursorValues();
            _statsRequests.OnNext(Unit.Default);
        }
        catch (Exception exception)
        {
            ClientLog.Warn("DataCenter", $"实时曲线补最近一段失败：{exception.Message}");
        }
    }

    /// <summary>
    /// 来了一帧：勾着的曲线各追加一个点（比后端留的那段更早的丢掉），横轴跟着的话往前挪。
    /// 帧上的轮次对不上说明后端重启过，重拉信号表。
    /// </summary>
    private void OnFrame(RealChartFrameDto frame)
    {
        if (frame.Session != _session)
        {
            _ = LoadLayoutAsync();
            return;
        }

        var time = DateTimeOffset.FromUnixTimeMilliseconds(frame.Time).LocalDateTime;
        double x = time.ToOADate();
        double keepFrom = time.AddSeconds(-_keepSeconds).ToOADate();
        foreach (var series in Series)
        {
            if (!_indexByName.TryGetValue(series.Name, out int index) || index >= frame.Values.Count)
            {
                continue;
            }

            // 补最近一段时已经带上的帧不重复加。
            if (series.Xs.Count > 0 && series.Xs[^1] >= x)
            {
                continue;
            }

            series.Append(x, frame.Values[index] is { } value ? value : double.NaN, keepFrom);
        }

        _latest = time;
        if (FollowRange)
        {
            SlideRange();
        }

        if (CursorTime is null)
        {
            UpdateCursorValues();
        }

        UpdateStatus();
        _statsRequests.OnNext(Unit.Default);
    }

    /// <summary>
    /// 整段挪到最新：最近 SelectedWindow 分钟。
    /// </summary>
    private void SlideRange()
    {
        var end = _latest ?? DateTime.Now;
        RangeStart = end.AddMinutes(-SelectedWindow);
        RangeEnd = end;
    }

    private void UpdateStatus()
    {
        Status = _latest is not { } latest
            ? L10n.Get("realchart.waiting")
            : FollowRange
                ? L10n.Get("realchart.live", latest)
                : L10n.Get("realchart.paused");
    }

    /// <summary>
    /// 光标处的值；鼠标不在图上就显示最新值。
    /// </summary>
    private void UpdateCursorValues()
    {
        double? x = CursorTime?.ToOADate();
        foreach (var series in Series)
        {
            series.CursorValue = x is { } time ? series.ValueAt(time) : series.LastValue;
        }
    }

    /// <summary>
    /// 当前看到那一段的最小 / 最大 / 平均（数据都在本地，现算）。
    /// </summary>
    private void UpdateStats()
    {
        if (VisibleStart is not { } start || VisibleEnd is not { } end)
        {
            return;
        }

        double from = start.ToOADate();
        double to = end.ToOADate();
        foreach (var series in Series)
        {
            (series.Min, series.Max, series.Avg) = series.StatsBetween(from, to);
        }
    }

    private static double ToLocalOADate(long time)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(time).LocalDateTime.ToOADate();
    }
}
