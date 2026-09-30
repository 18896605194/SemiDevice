using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;
using xyz.Client.Presentation.Models;
using PlotColor = ScottPlot.Color;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 趋势图：ScottPlot 5 的封装，数据曲线、实时曲线两页共用。
/// 喂给它一组 <see cref="TrendSeries"/>（Series），它负责：配色；纵轴只有左边一根，所有曲线都按真实数值画在它上面
/// （不按单位分轴，开关量也不另外分道——就是 0 和 1 两个位置上的阶梯线）；
/// 纵轴按当前看到的那段数据自动适配（中键框选放大后按框定住，双击恢复）。
/// 采样点不挤的时候每个点画一个小圆点；鼠标悬停时对准最近的采样时刻，每条线在这一刻的点放大高亮，旁边弹出数值框（时刻、点名、值）。
/// 横轴是时间：RangeStart/RangeEnd 是"整段"，FollowRange 为真时横轴跟着它走（实时曲线每秒挪一格就是这么挪的）；
/// 滚轮缩放、左键拖动平移、中键框选时 FollowRange 自动变假，双击回到整段并重新跟随。
/// 往外给：VisibleStart/VisibleEnd（当前看到的时间段）、CursorTime（对准的时刻，鼠标不在图上为 null）。
/// 页面隐藏时不画，重新显示再补画一次。
/// </summary>
public partial class TrendChart : UserControl
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IEnumerable), typeof(TrendChart),
        new PropertyMetadata(null, (d, e) => ((TrendChart)d).OnSeriesChanged(e.NewValue)));

    public static readonly DependencyProperty RangeStartProperty = DependencyProperty.Register(
        nameof(RangeStart), typeof(DateTime?), typeof(TrendChart),
        new PropertyMetadata(null, (d, _) => ((TrendChart)d).OnRangeChanged()));

    public static readonly DependencyProperty RangeEndProperty = DependencyProperty.Register(
        nameof(RangeEnd), typeof(DateTime?), typeof(TrendChart),
        new PropertyMetadata(null, (d, _) => ((TrendChart)d).OnRangeChanged()));

    public static readonly DependencyProperty FollowRangeProperty = DependencyProperty.Register(
        nameof(FollowRange), typeof(bool), typeof(TrendChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((TrendChart)d).OnRangeChanged()));

    public static readonly DependencyProperty VisibleStartProperty = DependencyProperty.Register(
        nameof(VisibleStart), typeof(DateTime?), typeof(TrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty VisibleEndProperty = DependencyProperty.Register(
        nameof(VisibleEnd), typeof(DateTime?), typeof(TrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CursorTimeProperty = DependencyProperty.Register(
        nameof(CursorTime), typeof(DateTime?), typeof(TrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// 纵轴自动适配时，数据的最小、最大值上下各留这么一点空（占数据跨度的比例），贴着边的线看得见。
    /// </summary>
    private const double ValueMargin = 0.05;

    /// <summary>
    /// 滚轮一格缩放的比例。
    /// </summary>
    private const double WheelZoom = 0.8;

    /// <summary>
    /// 纵轴最少撑开到数值大小的这个比例。
    /// </summary>
    private const double MinSpanRatio = 0.02;

    /// <summary>
    /// 时间轴最少看这么长（天）：10 秒。
    /// </summary>
    private const double MinTimeSpan = 10.0 / 86400;

    /// <summary>
    /// 相邻采样点至少隔开这么多像素才画圆点；点挤成一片时只画线。
    /// </summary>
    private const double MarkerSpacing = 6;

    private const float PointMarkerSize = 5;

    /// <summary>
    /// 悬停时对准的那个点放大到这么大。
    /// </summary>
    private const float HighlightMarkerSize = 11;

    /// <summary>
    /// 悬停时对准最近的采样点，离鼠标超过这么多像素就不对（没数据的空档里不弹数值框）。
    /// </summary>
    private const double SnapPixels = 40;

    /// <summary>
    /// 中键拖的框比这小（像素）就当没框。
    /// </summary>
    private const double MinBoxPixels = 6;

    /// <summary>
    /// 图上文字用的字体：挑一个中文和 ℃ 这类符号都显示得出来的（本机上是 Microsoft YaHei UI）。
    /// 不能写死 Microsoft YaHei——图表库按这个名字找不到字体，会退回没有中文的 Segoe UI，单位里的 ℃ 就成了方框。
    /// </summary>
    private static readonly string ChartFont = Fonts.Detect("温度℃");

    private readonly Plot _plot;

    private readonly Dictionary<TrendSeries, Scatter> _lines = [];

    /// <summary>
    /// 每条线一个放大的圆点，悬停时标出对准的那个采样点。
    /// </summary>
    private readonly Dictionary<TrendSeries, Marker> _highlights = [];

    /// <summary>
    /// 纵轴只有左边这一根：所有曲线（模拟量、开关量）都按真实数值画在它上面，不按单位分轴、不给开关量另外分道——
    /// 线在哪个刻度上，值就是多少。
    /// </summary>
    private readonly IYAxis _valueAxis;

    /// <summary>
    /// 纵轴平时的刻度（自动疏密）。
    /// </summary>
    private readonly NumericAutomatic _valueTicks = new();

    /// <summary>
    /// 图上只有开关量时纵轴的刻度：只标 0 和 1，中间的 0.2、0.4 对开关量没有意义。
    /// </summary>
    private readonly NumericManual _digitalTicks = new([0, 1], ["0", "1"]);

    /// <summary>
    /// 中键框选放大后定住的纵轴范围；没框过就是 null，纵轴自动适配。双击回到整段时清掉。
    /// </summary>
    private (double Min, double Max)? _manualRange;

    private readonly VerticalLine _cursorLine;

    private readonly DateTimeAutomatic _timeTicks;

    private INotifyCollectionChanged? _observedCollection;

    private bool _renderQueued;

    private bool _renderWhenVisible;

    private bool _fitPending = true;

    private double _timeSpanDays;

    private bool _dragging;

    private float _dragStartPixel;

    private (double Left, double Right) _dragStartLimits;

    private bool _boxing;

    private Pixel _boxStartPixel;

    private WpfPoint _boxStartPoint;

    /// <summary>
    /// 鼠标在绘图区里的位置（ScottPlot 像素 + 浮层坐标）：横轴自己在动（实时曲线往前走）时按它重新对准，数值框也跟着它摆。
    /// </summary>
    private Pixel? _mousePixel;

    private WpfPoint _mousePoint;

    public TrendChart()
    {
        InitializeComponent();

        _plot = PlotView.Plot;
        // 鼠标自己接管（只动时间轴、中键框选），ScottPlot 自带的拖拽缩放、右键菜单都关掉。
        PlotView.UserInputProcessor.Disable();

        var timeAxis = _plot.Axes.DateTimeTicksBottom();
        _timeTicks = (DateTimeAutomatic)timeAxis.TickGenerator;
        _timeTicks.LabelFormatter = FormatTime;

        _valueAxis = _plot.Axes.Left;
        _valueAxis.TickGenerator = _valueTicks;
        _plot.Axes.Right.IsVisible = false;

        _cursorLine = _plot.Add.VerticalLine(0);
        _cursorLine.LinePattern = LinePattern.Dashed;
        _cursorLine.LineWidth = 1;
        _cursorLine.IsVisible = false;

        ApplyTheme();

        // 按下、移动、松开走预览（隧道）事件：ScottPlot 控件在自己的鼠标松开里会先释放鼠标捕获，
        // 等冒泡到这儿拖动、框选已经被"丢了捕获"作废了——所以要赶在它前面收尾。
        PlotView.AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheel), true);
        PlotView.AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouseDown), true);
        PlotView.AddHandler(PreviewMouseUpEvent, new MouseButtonEventHandler(OnMouseUp), true);
        PlotView.AddHandler(PreviewMouseMoveEvent, new MouseEventHandler(OnMouseMove), true);
        PlotView.MouseLeave += (_, _) => HideHover();
        PlotView.LostMouseCapture += (_, _) => CancelGestures();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _renderWhenVisible)
            {
                RequestRender();
            }
        };
    }

    #region 依赖属性

    /// <summary>
    /// 要画的曲线（TrendSeries 的集合，ObservableCollection 增删会跟着变）。
    /// </summary>
    public IEnumerable? Series
    {
        get => (IEnumerable?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>
    /// 整段的起点（数据曲线是查询的时间段，实时曲线是最近 N 分钟）。
    /// </summary>
    public DateTime? RangeStart
    {
        get => (DateTime?)GetValue(RangeStartProperty);
        set => SetValue(RangeStartProperty, value);
    }

    public DateTime? RangeEnd
    {
        get => (DateTime?)GetValue(RangeEndProperty);
        set => SetValue(RangeEndProperty, value);
    }

    /// <summary>
    /// 横轴跟着整段走；用户缩放、平移、框选时自动变假（停在他看的地方），双击回到整段时变真。
    /// </summary>
    public bool FollowRange
    {
        get => (bool)GetValue(FollowRangeProperty);
        set => SetValue(FollowRangeProperty, value);
    }

    /// <summary>
    /// 当前看到的时间段（每次重画后更新）。
    /// </summary>
    public DateTime? VisibleStart
    {
        get => (DateTime?)GetValue(VisibleStartProperty);
        set => SetValue(VisibleStartProperty, value);
    }

    public DateTime? VisibleEnd
    {
        get => (DateTime?)GetValue(VisibleEndProperty);
        set => SetValue(VisibleEndProperty, value);
    }

    /// <summary>
    /// 鼠标对准的时刻（对准了采样点就是那个点的时刻）；鼠标不在绘图区为 null。
    /// </summary>
    public DateTime? CursorTime
    {
        get => (DateTime?)GetValue(CursorTimeProperty);
        set => SetValue(CursorTimeProperty, value);
    }

    #endregion

    #region 曲线与重画

    private void OnSeriesChanged(object? newValue)
    {
        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged -= OnCollectionChanged;
        }

        _observedCollection = newValue as INotifyCollectionChanged;
        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged += OnCollectionChanged;
        }

        RequestRender();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RequestRender();
    }

    private void OnSeriesDataChanged(object? sender, EventArgs e)
    {
        RequestRender();
    }

    private void OnRangeChanged()
    {
        if (FollowRange)
        {
            _fitPending = true;
            RequestRender();
        }
    }

    /// <summary>
    /// 攒一下再画：一帧里数据变、光标动、范围变只画一次。页面看不见就先记着，显示出来再画。
    /// </summary>
    private void RequestRender()
    {
        if (!IsVisible)
        {
            _renderWhenVisible = true;
            return;
        }

        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, Render);
    }

    private void Render()
    {
        _renderQueued = false;
        _renderWhenVisible = false;

        var series = CurrentSeries();
        SyncLines(series);
        if (_fitPending)
        {
            _fitPending = false;
            FitTime(series);
        }

        FitValues(series);
        UpdateMarkers(series);
        FollowMouse();
        PlotView.Refresh();
        PublishVisibleRange();
    }

    private List<TrendSeries> CurrentSeries()
    {
        return Series?.OfType<TrendSeries>().ToList() ?? [];
    }

    /// <summary>
    /// 曲线增删：新来的配色、建线（按引用画 TrendSeries 的两串数据）和它的悬停高亮点，走了的拆掉。
    /// 线都挂在左边那根纵轴上、按真实数值画；开关量画成阶梯线。
    /// </summary>
    private void SyncLines(List<TrendSeries> series)
    {
        foreach (var gone in _lines.Keys.Where(item => !series.Contains(item)).ToList())
        {
            _plot.Remove(_lines[gone]);
            _plot.Remove(_highlights[gone]);
            _lines.Remove(gone);
            _highlights.Remove(gone);
            gone.DataChanged -= OnSeriesDataChanged;
        }

        foreach (var item in series.Where(item => !_lines.ContainsKey(item)))
        {
            if (item.Color.A == 0)
            {
                item.Color = NextColor(series);
            }

            var color = ToPlotColor(item.Color);
            var line = _plot.Add.ScatterLine(item.Xs, item.Ys, color);
            line.LineWidth = 1.5f;
            line.MarkerShape = MarkerShape.FilledCircle;
            line.MarkerSize = 0;
            line.ConnectStyle = item.IsDigital ? ConnectStyle.StepHorizontal : ConnectStyle.Straight;
            _lines[item] = line;

            var highlight = _plot.Add.Marker(0, 0, MarkerShape.FilledCircle, HighlightMarkerSize, color);
            highlight.MarkerLineColor = ResourceColor("DarkPrimaryText");
            highlight.MarkerLineWidth = 1.5f;
            highlight.IsVisible = false;
            _highlights[item] = highlight;

            item.DataChanged += OnSeriesDataChanged;
        }

        // 高亮点、光标线始终在曲线上面。
        foreach (var highlight in _highlights.Values)
        {
            _plot.MoveToFront(highlight);
        }

        _plot.MoveToFront(_cursorLine);

        // 图上的线单位都一样就把单位标在轴上，不一样就不标（各自的单位看数值框和信息表）。
        var units = series.Select(item => item.Unit).Distinct().ToList();
        _valueAxis.IsVisible = series.Count > 0;
        _valueAxis.Label.Text = units.Count == 1 ? units[0] : string.Empty;
    }

    /// <summary>
    /// 横轴回到整段；没给整段就用数据的首尾，都没有就最近一小时。框选定住的纵轴也一并放开。
    /// </summary>
    private void FitTime(List<TrendSeries> series)
    {
        _manualRange = null;

        double left;
        double right;
        if (RangeStart is { } start && RangeEnd is { } end && end > start)
        {
            left = start.ToOADate();
            right = end.ToOADate();
        }
        else
        {
            var xs = series.Where(item => item.Xs.Count > 0).ToList();
            if (xs.Count > 0)
            {
                left = xs.Min(item => item.Xs[0]);
                right = xs.Max(item => item.Xs[^1]);
            }
            else
            {
                right = DateTime.Now.ToOADate();
                left = DateTime.Now.AddHours(-1).ToOADate();
            }

            if (right <= left)
            {
                right = left + 1.0 / 24 / 60;
            }
        }

        _plot.Axes.SetLimitsX(left, right);
    }

    /// <summary>
    /// 纵轴：取全部曲线在当前看到那段里的最小、最大值，上下各留一点空；框选定住的就用框的范围。
    /// 图上只有开关量时刻度只标 0 和 1。
    /// </summary>
    private void FitValues(List<TrendSeries> series)
    {
        var limits = _plot.Axes.GetLimits();
        _timeSpanDays = limits.Right - limits.Left;
        if (series.Count == 0)
        {
            return;
        }

        _valueAxis.TickGenerator = _manualRange is null && series.All(item => item.IsDigital)
            ? _digitalTicks
            : _valueTicks;

        var (min, max) = _manualRange ?? DataRange(series, limits.Left, limits.Right);
        double margin = _manualRange is null ? (max - min) * ValueMargin : 0;
        _plot.Axes.SetLimitsY(min - margin, max + margin, _valueAxis);
    }

    /// <summary>
    /// 这几条线在 [left, right] 里的最小、最大值。开关量只有 0、1 两个值，有它在就把 0~1 都包进来，
    /// 一直是 1（或一直是 0）的线也看得出是 1 还是 0。
    /// 纵轴至少撑开到数值大小的 2%：几乎不变的值不把噪声放大成大起大落，差一点点的两条线也不会被拉得老远；
    /// 全是 0 就给 0~1，线落在 0 刻度上，不往 0 以下撑。
    /// </summary>
    private static (double Min, double Max) DataRange(IEnumerable<TrendSeries> lines, double left, double right)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        foreach (var item in lines)
        {
            bool hasValue = false;
            for (int point = Math.Max(0, item.LowerBound(left) - 1); point < item.Xs.Count; point++)
            {
                double value = item.Ys[point];
                if (!double.IsNaN(value))
                {
                    hasValue = true;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }

                if (item.Xs[point] > right)
                {
                    break;
                }
            }

            if (hasValue && item.IsDigital)
            {
                min = Math.Min(min, 0);
                max = Math.Max(max, 1);
            }
        }

        if (min > max)
        {
            return (0, 1);
        }

        double magnitude = Math.Max(Math.Abs(min), Math.Abs(max));
        if (magnitude == 0)
        {
            return (0, 1);
        }

        double minSpan = magnitude * MinSpanRatio;
        if (max - min < minSpan)
        {
            double middle = (min + max) / 2;
            return (middle - minSpan / 2, middle + minSpan / 2);
        }

        return (min, max);
    }

    /// <summary>
    /// 采样点的小圆点：当前看到的这段里相邻两点隔得开（不少于 MarkerSpacing 像素）才画，点挤成一片就只画线。
    /// </summary>
    private void UpdateMarkers(List<TrendSeries> series)
    {
        var limits = _plot.Axes.GetLimits();
        double width = DataWidth();
        foreach (var item in series)
        {
            int count = item.UpperBound(limits.Right) - item.LowerBound(limits.Left);
            _lines[item].MarkerSize = count > 0 && width / count >= MarkerSpacing ? PointMarkerSize : 0;
        }
    }

    private void PublishVisibleRange()
    {
        var limits = _plot.Axes.GetLimits();
        var start = DateTime.FromOADate(limits.Left);
        var end = DateTime.FromOADate(limits.Right);
        if (VisibleStart != start)
        {
            SetCurrentValue(VisibleStartProperty, start);
        }

        if (VisibleEnd != end)
        {
            SetCurrentValue(VisibleEndProperty, end);
        }
    }

    #endregion

    #region 悬停：对准最近的采样点，高亮、弹数值框

    /// <summary>
    /// 鼠标在 x 处：对准最近的采样时刻（离鼠标太远就不对，比如停在没数据的空档里），
    /// 光标线落到这一刻，每条线在这一刻的点放大高亮，旁边弹出数值框。
    /// 数值框里的值是各曲线的 CursorValue——页面按 CursorTime 算好（跟信息表同一份）。
    /// </summary>
    private void UpdateHover(double x)
    {
        var series = CurrentSeries();
        double? snapped = null;
        double distance = double.MaxValue;
        foreach (var item in series)
        {
            int index = item.NearestIndex(x);
            if (index >= 0 && Math.Abs(item.Xs[index] - x) < distance)
            {
                distance = Math.Abs(item.Xs[index] - x);
                snapped = item.Xs[index];
            }
        }

        var limits = _plot.Axes.GetLimits();
        bool near = snapped is not null
                    && limits.Right > limits.Left
                    && distance / (limits.Right - limits.Left) * DataWidth() <= SnapPixels;
        double cursor = near ? snapped!.Value : x;

        _cursorLine.X = cursor;
        _cursorLine.IsVisible = true;
        SetCurrentValue(CursorTimeProperty, DateTime.FromOADate(cursor));

        foreach (var item in series)
        {
            var highlight = _highlights.GetValueOrDefault(item);
            if (highlight is null)
            {
                continue;
            }

            var value = near ? item.ValueAt(cursor) : null;
            highlight.IsVisible = value is not null;
            if (value is { } shown)
            {
                highlight.X = cursor;
                highlight.Y = shown;
            }
        }

        ShowHoverCard(near && series.Count > 0);
    }

    /// <summary>
    /// 数值框摆在鼠标右下；右边、下边放不下就翻到左边、往上挪。
    /// </summary>
    private void ShowHoverCard(bool show)
    {
        if (!show)
        {
            HoverCard.Visibility = Visibility.Collapsed;
            return;
        }

        HoverCard.Visibility = Visibility.Visible;
        HoverCard.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var size = HoverCard.DesiredSize;
        double left = _mousePoint.X + 16;
        if (left + size.Width > Overlay.ActualWidth)
        {
            left = Math.Max(0, _mousePoint.X - 16 - size.Width);
        }

        double top = _mousePoint.Y + 16;
        if (top + size.Height > Overlay.ActualHeight)
        {
            top = Math.Max(0, Overlay.ActualHeight - size.Height - 4);
        }

        Canvas.SetLeft(HoverCard, left);
        Canvas.SetTop(HoverCard, top);
    }

    /// <summary>
    /// 横轴自己动过（实时曲线往前走、查询结果换了），按鼠标所在的像素重新对准。
    /// </summary>
    private void FollowMouse()
    {
        if (_mousePixel is { } pixel && _cursorLine.IsVisible)
        {
            UpdateHover(_plot.GetCoordinates(pixel).X);
        }
    }

    private void HideHover()
    {
        _mousePixel = null;
        HoverCard.Visibility = Visibility.Collapsed;
        foreach (var highlight in _highlights.Values)
        {
            highlight.IsVisible = false;
        }

        if (!_cursorLine.IsVisible && CursorTime is null)
        {
            return;
        }

        _cursorLine.IsVisible = false;
        SetCurrentValue(CursorTimeProperty, null);
        RequestRender();
    }

    #endregion

    #region 鼠标：滚轮缩放、左键平移、中键框选、双击回到整段

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var limits = _plot.Axes.GetLimits();
        double center = _plot.GetCoordinates(PlotView.GetPlotPixelPosition(e)).X;
        double factor = e.Delta > 0 ? WheelZoom : 1 / WheelZoom;
        double left = center - (center - limits.Left) * factor;
        double right = center + (limits.Right - center) * factor;
        if (right - left < MinTimeSpan)
        {
            return;
        }

        StopFollowing();
        _plot.Axes.SetLimitsX(left, right);
        RequestRender();
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            // 双击：回到整段、重新跟随，框选定住的纵轴放开。
            CancelGestures();
            SetCurrentValue(FollowRangeProperty, true);
            _fitPending = true;
            RequestRender();
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            var limits = _plot.Axes.GetLimits();
            _dragging = true;
            _dragStartPixel = PlotView.GetPlotPixelPosition(e).X;
            _dragStartLimits = (limits.Left, limits.Right);
            PlotView.CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            _boxing = true;
            _boxStartPixel = PlotView.GetPlotPixelPosition(e);
            _boxStartPoint = e.GetPosition(Overlay);
            HideHover();
            DrawZoomBox(_boxStartPoint);
            ZoomBox.Visibility = Visibility.Visible;
            PlotView.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _dragging)
        {
            _dragging = false;
            PlotView.ReleaseMouseCapture();
        }
        else if (e.ChangedButton == MouseButton.Middle && _boxing)
        {
            _boxing = false;
            ZoomBox.Visibility = Visibility.Collapsed;
            PlotView.ReleaseMouseCapture();
            ZoomToBox(_boxStartPixel, PlotView.GetPlotPixelPosition(e));
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pixel = PlotView.GetPlotPixelPosition(e);
        var dataRect = _plot.LastRender.DataRect;
        if (_boxing)
        {
            DrawZoomBox(e.GetPosition(Overlay));
            return;
        }

        if (_dragging)
        {
            if (dataRect.Width <= 0)
            {
                return;
            }

            double perPixel = (_dragStartLimits.Right - _dragStartLimits.Left) / dataRect.Width;
            double shift = (pixel.X - _dragStartPixel) * perPixel;
            if (Math.Abs(pixel.X - _dragStartPixel) >= 2)
            {
                StopFollowing();
            }

            _plot.Axes.SetLimitsX(_dragStartLimits.Left - shift, _dragStartLimits.Right - shift);
            HideHover();
            RequestRender();
            return;
        }

        if (!dataRect.Contains(pixel.X, pixel.Y))
        {
            HideHover();
            return;
        }

        _mousePixel = pixel;
        _mousePoint = e.GetPosition(Overlay);
        UpdateHover(_plot.GetCoordinates(pixel).X);
        RequestRender();
    }

    /// <summary>
    /// 拖着的时候画框：框限在绘图区里（ScottPlot 像素按显示缩放换成界面坐标）。
    /// </summary>
    private void DrawZoomBox(WpfPoint current)
    {
        double scale = PlotView.DisplayScale > 0 ? PlotView.DisplayScale : 1;
        var rect = _plot.LastRender.DataRect;
        double left = Math.Clamp(Math.Min(_boxStartPoint.X, current.X), rect.Left / scale, rect.Right / scale);
        double right = Math.Clamp(Math.Max(_boxStartPoint.X, current.X), rect.Left / scale, rect.Right / scale);
        double top = Math.Clamp(Math.Min(_boxStartPoint.Y, current.Y), rect.Top / scale, rect.Bottom / scale);
        double bottom = Math.Clamp(Math.Max(_boxStartPoint.Y, current.Y), rect.Top / scale, rect.Bottom / scale);
        Canvas.SetLeft(ZoomBox, left);
        Canvas.SetTop(ZoomBox, top);
        ZoomBox.Width = right - left;
        ZoomBox.Height = bottom - top;
    }

    /// <summary>
    /// 中键框选放大：时间轴放大到框住的那段，纵轴也定到框住的数值范围（框得太扁就只放大时间）。
    /// 之后缩放、平移都不动这个纵轴范围，双击回到整段才放开。
    /// </summary>
    private void ZoomToBox(Pixel from, Pixel to)
    {
        var rect = _plot.LastRender.DataRect;
        float left = Math.Clamp(Math.Min(from.X, to.X), rect.Left, rect.Right);
        float right = Math.Clamp(Math.Max(from.X, to.X), rect.Left, rect.Right);
        float top = Math.Clamp(Math.Min(from.Y, to.Y), rect.Top, rect.Bottom);
        float bottom = Math.Clamp(Math.Max(from.Y, to.Y), rect.Top, rect.Bottom);
        if (right - left < MinBoxPixels)
        {
            return;
        }

        double timeFrom = _plot.GetCoordinates(new Pixel(left, top)).X;
        double timeTo = _plot.GetCoordinates(new Pixel(right, top)).X;
        if (timeTo - timeFrom < MinTimeSpan)
        {
            double middle = (timeFrom + timeTo) / 2;
            timeFrom = middle - MinTimeSpan / 2;
            timeTo = middle + MinTimeSpan / 2;
        }

        StopFollowing();
        _plot.Axes.SetLimitsX(timeFrom, timeTo);

        if (bottom - top >= MinBoxPixels && _valueAxis.IsVisible)
        {
            double low = _valueAxis.GetCoordinate(bottom, rect);
            double high = _valueAxis.GetCoordinate(top, rect);
            if (high > low)
            {
                _manualRange = (low, high);
            }
        }

        RequestRender();
    }

    /// <summary>
    /// 鼠标被抢走（切窗口、弹框）时，拖着的、框着的都作废。
    /// </summary>
    private void CancelGestures()
    {
        _dragging = false;
        if (_boxing)
        {
            _boxing = false;
            ZoomBox.Visibility = Visibility.Collapsed;
        }
    }

    private void StopFollowing()
    {
        if (FollowRange)
        {
            SetCurrentValue(FollowRangeProperty, false);
        }
    }

    /// <summary>
    /// 绘图区宽（ScottPlot 像素）；还没画过时按控件宽估。
    /// </summary>
    private double DataWidth()
    {
        double width = _plot.LastRender.DataRect.Width;
        return width > 0 ? width : PlotView.ActualWidth * Math.Max(1, PlotView.DisplayScale);
    }

    #endregion

    #region 外观

    /// <summary>
    /// 暗色主题：颜色全从 DarkColors.xaml 取；字体用 ChartFont，单位里有中文、℃ 也显示得出来。
    /// </summary>
    private void ApplyTheme()
    {
        _plot.FigureBackground.Color = ResourceColor("DarkSurfaceBackground");
        _plot.DataBackground.Color = ResourceColor("DarkWindowBackground");
        _plot.Grid.MajorLineColor = ResourceColor("DarkChartGrid");
        _plot.Font.Set(ChartFont);
        foreach (var axis in _plot.Axes.GetAxes())
        {
            StyleAxis(axis);
        }

        _plot.Axes.FrameColor(ResourceColor("DarkBorderBrush"));
        _cursorLine.Color = ResourceColor("DarkPrimaryText");
    }

    private void StyleAxis(IAxis axis)
    {
        var text = ResourceColor("DarkChartAxisText");
        axis.Label.ForeColor = text;
        axis.Label.FontName = ChartFont;
        axis.TickLabelStyle.ForeColor = text;
        axis.TickLabelStyle.FontName = ChartFont;
        axis.MajorTickStyle.Color = text;
        axis.MinorTickStyle.Color = text;
        axis.FrameLineStyle.Color = ResourceColor("DarkBorderBrush");
    }

    /// <summary>
    /// 时间刻度：看的时间段超过一天带上日期，一天以内只要时分秒。
    /// </summary>
    private string FormatTime(DateTime time)
    {
        return _timeSpanDays > 1
            ? time.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
            : time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 调色板里挑一个眼下没被别的曲线占着的颜色；都占了就按条数轮。
    /// </summary>
    private WpfColor NextColor(List<TrendSeries> series)
    {
        var palette = TryFindResource("TrendPalette") as WpfColor[] ?? [];
        if (palette.Length == 0)
        {
            return System.Windows.Media.Colors.DeepSkyBlue;
        }

        var used = series.Where(item => item.Color.A != 0).Select(item => item.Color).ToHashSet();
        foreach (var color in palette)
        {
            if (!used.Contains(color))
            {
                return color;
            }
        }

        return palette[series.Count % palette.Length];
    }

    private PlotColor ResourceColor(string key)
    {
        return TryFindResource(key) is System.Windows.Media.SolidColorBrush brush
            ? ToPlotColor(brush.Color)
            : PlotColor.FromHex("#9E9E9E");
    }

    private static PlotColor ToPlotColor(WpfColor color)
    {
        return new PlotColor(color.R, color.G, color.B, color.A);
    }

    #endregion
}
