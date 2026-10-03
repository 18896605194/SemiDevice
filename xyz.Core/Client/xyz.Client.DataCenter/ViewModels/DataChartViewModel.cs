using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using ProtoBuf.Grpc;
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

namespace xyz.Client.DataCenter.ViewModels;

/// <summary>
/// 数据曲线页 ViewModel：左边信号树勾选，右边画一段时间的曲线（后端每秒入库的那张宽表），
/// 右下角每条曲线一行：颜色、名称、单位、光标处的值、显示时间段内的最小 / 最大 / 平均（后端按原始数据算）。
///
/// 查询是一条 Rx 管道：点查询、勾了新信号立刻查；缩放、平移停下 400ms 后按看到的那一段再查一次
/// （点多了后端抽稀，看得越细点越密，统计也跟着换成看到的这一段）；
/// 新查询一来，还没回来的旧查询直接取消（Switch），界面上只落最后一次的结果。
/// </summary>
public class DataChartViewModel : BaseViewModel
{
    /// <summary>
    /// 缩放、平移停下多久才按看到的那段重查。
    /// </summary>
    private static readonly TimeSpan ZoomSettle = TimeSpan.FromMilliseconds(400);

    #region Column

    /// <summary>
    /// 信号勾选树（模块 → 部件 → 属性）。
    /// </summary>
    public ObservableCollection<SignalNodeModel> Signals { get; } = [];

    /// <summary>
    /// 勾上的曲线，按勾选先后：图和右下角信息表都绑它。
    /// </summary>
    public ObservableCollection<TrendSeries> Series { get; } = [];

    private DateTime? _startTime = QueryDateRange.LastHours(1).Start;

    /// <summary>
    /// 起始时刻（精确到分钟）。
    /// </summary>
    public DateTime? StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value))
            {
                _defaultTimes = false;
            }
        }
    }

    private DateTime? _endTime = QueryDateRange.LastHours(1).End;

    /// <summary>
    /// 截止时刻（精确到分钟，这一分钟整分钟都算进去）。
    /// </summary>
    public DateTime? EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, value))
            {
                _defaultTimes = false;
            }
        }
    }

    private DateTime? _rangeStart;

    /// <summary>
    /// 图的"整段"：最近一次点查询的时间段，双击图回到这一段。
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

    private bool _followRange = true;

    /// <summary>
    /// 图的横轴是否跟着整段（用户缩放、平移后为假，双击图回到整段为真）。
    /// </summary>
    public bool FollowRange
    {
        get => _followRange;
        set => SetProperty(ref _followRange, value);
    }

    private DateTime? _visibleStart;

    /// <summary>
    /// 图上当前看到的时间段（图控件回写）：变了就按它重查。
    /// </summary>
    public DateTime? VisibleStart
    {
        get => _visibleStart;
        set
        {
            if (SetProperty(ref _visibleStart, value))
            {
                FetchVisibleRange();
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
                FetchVisibleRange();
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
    /// 最多同时画几条（后端 sc.xml 的 DataChart 节点 QueryMaxSignals）。
    /// </summary>
    public int MaxSignals
    {
        get => _maxSignals;
        private set => SetProperty(ref _maxSignals, value);
    }

    private string _summary = L10n.Get("common.query_hint");

    /// <summary>
    /// 工具栏右侧的说明：查询中、多少个时间点、是否抽稀、失败原因。
    /// </summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    #endregion

    #region Command

    public IRelayCommand QueryCommand { get; }

    public IRelayCommand LastHourCommand { get; }

    public IRelayCommand TodayCommand { get; }

    public IRelayCommand ClearCommand { get; }

    public IAsyncRelayCommand RefreshSignalsCommand { get; }

    #endregion

    #region Service

    private readonly IDataChartService _service;

    /// <summary>
    /// 管道入口：立刻查（点查询、勾了新信号）。
    /// </summary>
    private readonly Subject<ChartFetch> _fetchNow = new();

    /// <summary>
    /// 管道入口：停下再查（缩放、平移）。
    /// </summary>
    private readonly Subject<ChartFetch> _fetchSettled = new();

    private readonly Dictionary<string, TrendSeries> _seriesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 最近一次发出去的查询时间段：图上看到的跟它一样就不用再查。
    /// </summary>
    private (DateTime Start, DateTime End)? _fetchedRange;

    /// <summary>
    /// 工具栏上的时间还是打开软件时给的默认"最近 1 小时"，没人改过：还没查过就勾信号时，按现在重新取最近 1 小时。
    /// </summary>
    private bool _defaultTimes = true;

    private IDisposable? _pipeline;

    private IDisposable? _reconnect;

    #endregion

    public DataChartViewModel()
    {
        _service = GrpcClientFactory.Create<IDataChartService>();

        QueryCommand = new RelayCommand(DoQuery);
        LastHourCommand = new RelayCommand(DoLastHour);
        TodayCommand = new RelayCommand(DoToday);
        ClearCommand = new RelayCommand(DoClear);
        RefreshSignalsCommand = new AsyncRelayCommand(DoRefreshSignals);
    }

    /// <summary>
    /// 搭查询管道、拉信号表；后端晚起（没拉到信号表）时，等事件流连上再拉一次。
    /// </summary>
    public override void Init()
    {
        var ui = SynchronizationContext.Current;
        IObservable<ChartFetch> settled = _fetchSettled.Throttle(ZoomSettle);
        if (ui is not null)
        {
            // 停下后回到界面线程再发查询。
            settled = settled.ObserveOn(ui);
        }

        var results = _fetchNow
            .Merge(settled)
            .Select(fetch => Observable.FromAsync(token => FetchAsync(fetch, token)))
            .Switch();
        if (ui is not null)
        {
            // 结果要落到界面绑定的曲线上。
            results = results.ObserveOn(ui);
        }

        _pipeline?.Dispose();
        _pipeline = results.Subscribe(Apply);

        _reconnect?.Dispose();
        _reconnect = Observable
            .FromEvent<bool>(handler => RemoteEventBus.ConnectionChanged += handler,
                handler => RemoteEventBus.ConnectionChanged -= handler)
            .Where(connected => connected && Signals.Count == 0)
            .Subscribe(connected => _ = LoadSignalsAsync());

        _ = LoadSignalsAsync();
    }

    private void DoQuery()
    {
        var range = QueryDateRange.Of(StartTime ?? QueryDateRange.LastHours(1).Start, EndTime ?? QueryDateRange.LastHours(1).End);
        RangeStart = range.Start;
        RangeEnd = range.End;
        FollowRange = true;
        RequestFetch(range.Start, range.End, now: true);
    }

    private void DoLastHour()
    {
        (StartTime, EndTime) = QueryDateRange.LastHours(1);
        DoQuery();
    }

    private void DoToday()
    {
        (StartTime, EndTime) = QueryDateRange.Today();
        DoQuery();
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
        return LoadSignalsAsync();
    }

    /// <summary>
    /// 拉信号表、重长勾选树；已经勾着的照样勾着。
    /// </summary>
    private async Task LoadSignalsAsync()
    {
        try
        {
            var response = await _service.GetSignalsAsync(new RpcRequest());
            if (!response.Success)
            {
                Summary = L10n.Get("chart.load_failed", L10n.Get(response.Code, response.Args));
                return;
            }

            var result = response.DeserializeData<DataChartSignalsDto>();
            MaxSignals = Math.Max(1, result.MaxSignals);
            Signals.Clear();
            foreach (var root in SignalTree.Build(result.Signals, OnToggle))
            {
                Signals.Add(root);
            }

            foreach (var leaf in SignalTree.AllLeaves(Signals))
            {
                leaf.SetChecked(_seriesByName.ContainsKey(leaf.Path));
            }

            SignalTree.RefreshGroups(Signals);
            SignalTree.Filter(Signals, SearchText);
        }
        catch (Exception exception)
        {
            Summary = L10n.Get("chart.load_failed", exception.Message);
            ClientLog.Warn("DataCenter", $"数据曲线信号表没拿到：{exception.Message}");
        }
    }

    /// <summary>
    /// 勾选树上点了一下：勾模块、部件就是勾下面全部（搜索时只动看得见的）；超过最多条数整次不勾，给提示。
    /// 勾了新的就按当前的时间段查一次；去掉的直接从图上拿掉，不用查。
    /// </summary>
    private void OnToggle(SignalNodeModel node, bool check)
    {
        var leaves = node.Leaves().Where(leaf => leaf.IsShown).ToList();
        if (check)
        {
            var adding = leaves.Where(leaf => leaf.IsChecked != true).ToList();
            if (Series.Count + adding.Count > MaxSignals)
            {
                Summary = L10n.Get("chart.too_many", MaxSignals);
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
        if (check)
        {
            var range = _fetchedRange;
            if (range is not null)
            {
                RequestFetch(range.Value.Start, range.Value.End, now: true);
            }
            else if (_defaultTimes)
            {
                // 还没查过、时间也没人动过：按现在的最近 1 小时查（默认值是打开软件那会儿算的，早过时了）。
                DoLastHour();
            }
            else
            {
                // 还没查过：按工具栏上选的时间段查，图也回到这一段。
                DoQuery();
            }
        }
    }

    /// <summary>
    /// 图上看到的时间段变了（缩放、平移、回到整段）：跟最近一次查的不一样才重查，停下再查。
    /// </summary>
    private void FetchVisibleRange()
    {
        var start = VisibleStart;
        var end = VisibleEnd;
        if (start is null || end is null || end.Value <= start.Value || Series.Count == 0)
        {
            return;
        }

        var fetched = _fetchedRange;
        if (fetched is not null
            && Math.Abs((fetched.Value.Start - start.Value).TotalSeconds) < 1
            && Math.Abs((fetched.Value.End - end.Value).TotalSeconds) < 1)
        {
            return;
        }

        RequestFetch(start.Value, end.Value, now: false);
    }

    private void RequestFetch(DateTime start, DateTime end, bool now)
    {
        _fetchedRange = (start, end);
        var names = Series.Select(series => series.Name).ToArray();
        if (names.Length == 0)
        {
            return;
        }

        Summary = L10n.Get("datachart.querying");
        (now ? _fetchNow : _fetchSettled).OnNext(new ChartFetch(start, end, names));
    }

    /// <summary>
    /// 查一次。出错不往管道里抛（抛了整条管道就断了），带着错误信息回去由 Apply 显示；被新查询顶掉时 token 取消，结果没人要。
    /// </summary>
    private async Task<ChartOutcome> FetchAsync(ChartFetch fetch, CancellationToken token)
    {
        try
        {
            var response = await _service.QueryAsync(
                new DataChartQuery { Start = fetch.Start, End = fetch.End, Names = fetch.Names.ToList() },
                new CallContext(new CallOptions(cancellationToken: token)));
            return response.Success
                ? new ChartOutcome(response.DeserializeData<DataChartResultDto>(), null, null)
                : new ChartOutcome(null, response.Code, response.Args);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            return new ChartOutcome(null, null, [exception.Message]);
        }
    }

    /// <summary>
    /// 结果落到曲线上：时间轴共用一条（UTC 毫秒 → 本机时间），null 变 NaN（曲线断开）；统计用后端按原始数据算的。
    /// 这期间被去掉的曲线不管，新勾的等它自己那次查询。
    /// </summary>
    private void Apply(ChartOutcome outcome)
    {
        var result = outcome.Result;
        if (result is null)
        {
            var code = outcome.Code;
            string reason = code is not null && code.Length > 0
                ? L10n.Get(code, outcome.Args ?? [])
                : string.Join(" ", outcome.Args ?? []);
            Summary = L10n.Get("common.query_failed");
            ClientLog.Error("DataCenter", $"数据曲线查询失败：{reason}");
            return;
        }

        var xs = result.Times
            .Select(time => DateTimeOffset.FromUnixTimeMilliseconds(time).LocalDateTime.ToOADate())
            .ToArray();
        foreach (var item in result.Series)
        {
            if (!_seriesByName.TryGetValue(item.Name, out var series))
            {
                continue;
            }

            series.SetData(xs, item.Values.Select(value => value is not null ? value.Value : double.NaN));
            series.Min = item.Min;
            series.Max = item.Max;
            series.Avg = item.Avg;
        }

        UpdateCursorValues();
        Summary = result.Series.All(item => item.Count == 0)
            ? L10n.Get("datachart.no_data")
            : result.IsDecimated
                ? L10n.Get("datachart.decimated", Math.Round(result.BucketMs / 1000.0, 1))
                : L10n.Get("datachart.points", result.Times.Count);
    }

    private void UpdateCursorValues()
    {
        double? x = CursorTime?.ToOADate();
        foreach (var series in Series)
        {
            series.CursorValue = x is not null ? series.ValueAt(x.Value) : null;
        }
    }

    /// <summary>
    /// 一次查询：时间段 + 当时勾着的信号名（在界面线程上拍下来，不在后台线程读界面的东西）。
    /// </summary>
    private sealed record ChartFetch(DateTime Start, DateTime End, string[] Names);

    /// <summary>
    /// 查询结果或失败原因（错误码 + 参数，界面线程上再翻成当前语言）。
    /// </summary>
    private sealed record ChartOutcome(DataChartResultDto? Result, string? Code, List<string>? Args);
}
