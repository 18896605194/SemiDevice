using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.Plottables;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using PlotColor = ScottPlot.Color;
using WpfCursor = System.Windows.Input.Cursor;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfVector = System.Windows.Vector;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 趋势图的基准线（右上角 X、Y 两个按钮开关）：X 两条竖线量时间差，Y 两条横线量数值差。
/// 线比曲线粗、画在曲线下面；按住线（6 像素以内）或轴上的标签就能拖，离线远的地方左键照旧平移。
/// 线钉在数据上（X 是时刻、Y 是纵轴上的值），缩放、平移以后还在原处；打开时原来的位置不在眼前，就放到当前这一段的 1/3、2/3。
/// X1、X2 的时刻标在时间轴上，Y1、Y2 的值标在纵轴上（被压住的刻度不写字）；两条线之间一个双向箭头标出差（ΔX、ΔY）。
/// 开着 X 线时，每条曲线在 X1、X2 那一刻的值写进 TrendSeries.ValueAtX1 / ValueAtX2，信息表从那儿显示。
/// </summary>
public partial class TrendChart
{
    public static readonly DependencyProperty ShowXLinesProperty = DependencyProperty.Register(
        nameof(ShowXLines), typeof(bool), typeof(TrendChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((TrendChart)d).OnShowXLinesChanged()));

    public static readonly DependencyProperty ShowYLinesProperty = DependencyProperty.Register(
        nameof(ShowYLines), typeof(bool), typeof(TrendChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((TrendChart)d).OnShowYLinesChanged()));

    /// <summary>
    /// 基准线的线宽：比曲线（1.5）粗一点，一眼跟曲线分开。
    /// </summary>
    private const float ReferenceLineWidth = 2.5f;

    /// <summary>
    /// 鼠标指着、正拖着的那一条再粗一点。
    /// </summary>
    private const float ActiveReferenceLineWidth = 3.5f;

    /// <summary>
    /// 鼠标离线这么多像素以内就算按在线上。
    /// </summary>
    private const double GrabPixels = 6;

    /// <summary>
    /// 两个标签挨着时至少隔开这么多（界面坐标）。
    /// </summary>
    private const double TagSpacing = 6;

    /// <summary>
    /// 纵轴上的 Y 标签离图的左边至少空这么多（界面坐标），不贴着边。
    /// </summary>
    private const double AxisTagMargin = 2;

    /// <summary>
    /// 双向箭头的箭尖长（界面坐标）。
    /// </summary>
    private const double ArrowLength = 5;

    /// <summary>
    /// 双向箭头的箭尖半宽（界面坐标）。
    /// </summary>
    private const double ArrowHalfWidth = 3.5;

    /// <summary>
    /// 两条线隔得比这近（界面坐标）就不画箭头，只标差。
    /// </summary>
    private const double MinArrowLength = 14;

    /// <summary>
    /// ΔX 标在绘图区顶上往下这么多（界面坐标）。
    /// </summary>
    private const double DeltaXInset = 14;

    /// <summary>
    /// ΔY 的箭头离绘图区右边这么多（界面坐标）。
    /// </summary>
    private const double DeltaYInset = 46;

    /// <summary>
    /// 差值放在两条线中间时两边至少还要空出这么多（界面坐标），不够就放到线外面。
    /// </summary>
    private const double DeltaRoom = 12;

    /// <summary>
    /// 绘图区上面留的空（界面坐标）：右上角那排按钮（基准线开关、截图、放大）浮在这条空里，不挡曲线。
    /// </summary>
    private const double TopMargin = 40;

    /// <summary>
    /// 放 Y 线时值取到纵轴范围的几分之一那一位：范围 0~200 取到 0.1。
    /// </summary>
    private const double ValueResolutionParts = 500;

    private readonly VerticalLine _x1Line;

    private readonly VerticalLine _x2Line;

    private readonly HorizontalLine _y1Line;

    private readonly HorizontalLine _y2Line;

    /// <summary>
    /// 时间轴、纵轴的刻度都包一层：基准线标签压住的刻度不写字。
    /// </summary>
    private readonly TrendTickFilter _timeTickFilter;

    private readonly TrendTickFilter _valueTickFilter;

    /// <summary>
    /// 两条 X 线的时刻（OADate）；还没放过是 NaN。
    /// </summary>
    private double _x1 = double.NaN;

    private double _x2 = double.NaN;

    /// <summary>
    /// 两条 Y 线在纵轴上的值（画出来的位置，曲线挪过就不是它的真实值）；还没放过是 NaN。
    /// </summary>
    private double _y1 = double.NaN;

    private double _y2 = double.NaN;

    private TrendReferenceLine _lineDragging = TrendReferenceLine.None;

    private TrendReferenceLine _lineUnderMouse = TrendReferenceLine.None;

    private PlotColor _referenceColor;

    private PlotColor _referenceActiveColor;

    /// <summary>
    /// 开着 Y 线时纵轴至少这么宽（像素），放得下 Y1、Y2 的标签；只撑大不缩回，拖线时绘图区不跟着来回跳。
    /// </summary>
    private float _valueAxisMinimum;

    /// <summary>
    /// 显示 X 基准线（两条竖线）；右上角 X 按钮开关。
    /// </summary>
    public bool ShowXLines
    {
        get => (bool)GetValue(ShowXLinesProperty);
        set => SetValue(ShowXLinesProperty, value);
    }

    /// <summary>
    /// 显示 Y 基准线（两条横线）；右上角 Y 按钮开关。
    /// </summary>
    public bool ShowYLines
    {
        get => (bool)GetValue(ShowYLinesProperty);
        set => SetValue(ShowYLinesProperty, value);
    }

    #region 开关、每次重画

    /// <summary>
    /// 一条基准线的初始样子：不参与自动缩放、不进图例、先藏着，放到曲线下面。
    /// </summary>
    private T PrepareReferenceLine<T>(T line) where T : AxisLine
    {
        line.LineWidth = ReferenceLineWidth;
        line.EnableAutoscale = false;
        line.ExcludeFromLegend = true;
        line.IsVisible = false;
        _plot.MoveToBack(line);
        return line;
    }

    /// <summary>
    /// X 线打开：原来的两条都还在眼前就放回原处，不然放到当前这一段的 1/3、2/3（对准最近的采样点）。关掉：手上拖着的放下。
    /// </summary>
    private void OnShowXLinesChanged()
    {
        if (ShowXLines)
        {
            var limits = _plot.Axes.GetLimits();
            if (!Within(_x1, limits.Left, limits.Right) || !Within(_x2, limits.Left, limits.Right))
            {
                var series = CurrentSeries();
                double span = limits.Right - limits.Left;
                _x1 = SnapTime(series, limits.Left + span / 3);
                _x2 = SnapTime(series, limits.Left + span * 2 / 3);
            }
        }
        else
        {
            DropLines(TrendReferenceLine.X1, TrendReferenceLine.X2);
        }

        RequestRender();
    }

    /// <summary>
    /// Y 线打开：原来的两条都还在眼前就放回原处，不然放到当前纵轴范围的 1/3、2/3。关掉：手上拖着的放下。
    /// </summary>
    private void OnShowYLinesChanged()
    {
        if (ShowYLines)
        {
            var limits = _plot.Axes.GetLimits();
            if (!Within(_y1, limits.Bottom, limits.Top) || !Within(_y2, limits.Bottom, limits.Top))
            {
                double span = limits.Top - limits.Bottom;
                _y1 = RoundValue(limits.Bottom + span / 3, span);
                _y2 = RoundValue(limits.Bottom + span * 2 / 3, span);
            }
        }
        else
        {
            DropLines(TrendReferenceLine.Y1, TrendReferenceLine.Y2);
        }

        RequestRender();
    }

    /// <summary>
    /// 关掉的那两条线：手上正拖着就放下，鼠标指着就不再算指着。
    /// </summary>
    private void DropLines(TrendReferenceLine first, TrendReferenceLine second)
    {
        if (_lineDragging == first || _lineDragging == second)
        {
            _lineDragging = TrendReferenceLine.None;
            PlotView.ReleaseMouseCapture();
        }

        if (_lineUnderMouse == first || _lineUnderMouse == second)
        {
            _lineUnderMouse = TrendReferenceLine.None;
            PlotView.Cursor = Cursors.Cross;
        }
    }

    /// <summary>
    /// 每次重画：四条线的位置、粗细、颜色；标签、差值的字；开着 X 线时每条曲线在 X1、X2 那一刻的值；
    /// 再按标签的大小告诉两根轴哪几个刻度不写字，纵轴撑到放得下 Y 标签，上边留出按钮那一条。
    /// </summary>
    private void UpdateReferenceLines(List<TrendSeries> series)
    {
        var limits = _plot.Axes.GetLimits();
        var active = _lineDragging != TrendReferenceLine.None ? _lineDragging : _lineUnderMouse;
        ApplyLine(_x1Line, ShowXLines, _x1, active == TrendReferenceLine.X1);
        ApplyLine(_x2Line, ShowXLines, _x2, active == TrendReferenceLine.X2);
        ApplyLine(_y1Line, ShowYLines, _y1, active == TrendReferenceLine.Y1);
        ApplyLine(_y2Line, ShowYLines, _y2, active == TrendReferenceLine.Y2);

        if (ShowXLines)
        {
            foreach (var item in series)
            {
                item.ValueAtX1 = double.IsNaN(_x1) ? null : item.ValueAt(_x1);
                item.ValueAtX2 = double.IsNaN(_x2) ? null : item.ValueAt(_x2);
            }
        }

        X1TagText.Text = "X1 " + FormatTagTime(_x1);
        X2TagText.Text = "X2 " + FormatTagTime(_x2);
        Y1TagText.Text = "Y1 " + FormatValue(_y1);
        Y2TagText.Text = "Y2 " + FormatValue(_y2);
        XDeltaText.Text = "ΔX " + FormatDuration(_x2 - _x1);
        YDeltaText.Text = "ΔY " + FormatValue(Math.Abs(_y2 - _y1));
        X1Tag.Tag = active == TrendReferenceLine.X1;
        X2Tag.Tag = active == TrendReferenceLine.X2;
        Y1Tag.Tag = active == TrendReferenceLine.Y1;
        Y2Tag.Tag = active == TrendReferenceLine.Y2;

        // 线不在眼前，它的标签就不显示；差值和箭头摆的时候再看
        ShowOverlay(X1Tag, ShowXLines && Within(_x1, limits.Left, limits.Right));
        ShowOverlay(X2Tag, ShowXLines && Within(_x2, limits.Left, limits.Right));
        ShowOverlay(Y1Tag, ShowYLines && Within(_y1, limits.Bottom, limits.Top));
        ShowOverlay(Y2Tag, ShowYLines && Within(_y2, limits.Bottom, limits.Top));

        UpdateTickBlocks();
    }

    private void ApplyLine(AxisLine line, bool show, double position, bool active)
    {
        line.IsVisible = show && !double.IsNaN(position);
        if (!line.IsVisible)
        {
            return;
        }

        line.Position = position;
        line.LineWidth = active ? ActiveReferenceLineWidth : ReferenceLineWidth;
        line.LineColor = active ? _referenceActiveColor : _referenceColor;
    }

    /// <summary>
    /// 告诉两根轴标签压着哪几段：标签先量好大小（界面坐标），换成像素给刻度那边算。
    /// 纵轴开着 Y 线时撑到放得下 Y 标签；上边一直留出按钮那一条。
    /// </summary>
    private void UpdateTickBlocks()
    {
        var rect = _plot.LastRender.DataRect;
        double scale = PixelScale();
        var timeBlocks = new List<(double Position, double Offset, double HalfLength)>();
        var valueBlocks = new List<(double Position, double Offset, double HalfLength)>();
        if (rect.Width > 0 && rect.Height > 0)
        {
            if (ShowXLines)
            {
                double first = _plot.Axes.Bottom.GetPixel(_x1, rect);
                double second = _plot.Axes.Bottom.GetPixel(_x2, rect);
                double firstWidth = MeasureOverlay(X1Tag).Width * scale;
                double secondWidth = MeasureOverlay(X2Tag).Width * scale;
                var centers = Spread(first, second, (firstWidth + secondWidth) / 2 + TagSpacing * scale);
                AddBlock(timeBlocks, X1Tag, _x1, centers.First - first, firstWidth / 2);
                AddBlock(timeBlocks, X2Tag, _x2, centers.Second - second, secondWidth / 2);
            }

            if (ShowYLines)
            {
                double first = _valueAxis.GetPixel(_y1, rect);
                double second = _valueAxis.GetPixel(_y2, rect);
                var firstSize = MeasureOverlay(Y1Tag);
                var secondSize = MeasureOverlay(Y2Tag);
                var centers = Spread(first, second, (firstSize.Height + secondSize.Height) / 2 * scale + TagSpacing * scale);

                // 纵轴的像素往下变大、坐标往上变大，挪的方向反过来
                AddBlock(valueBlocks, Y1Tag, _y1, first - centers.First, firstSize.Height * scale / 2);
                AddBlock(valueBlocks, Y2Tag, _y2, second - centers.Second, secondSize.Height * scale / 2);
                double widest = Math.Max(firstSize.Width, secondSize.Width) + AxisTagMargin;
                _valueAxisMinimum = Math.Max(_valueAxisMinimum, (float)(widest * scale));
            }
        }

        if (!ShowYLines)
        {
            _valueAxisMinimum = 0;
        }

        _timeTickFilter.Blocks = timeBlocks;
        _valueTickFilter.Blocks = valueBlocks;
        _valueAxis.MinimumSize = _valueAxisMinimum;
        _plot.Axes.Top.MinimumSize = (float)(TopMargin * scale);
    }

    private static void AddBlock(
        List<(double Position, double Offset, double HalfLength)> blocks, FrameworkElement tag, double position, double offset, double halfLength)
    {
        if (tag.Visibility == Visibility.Visible)
        {
            blocks.Add((position, offset, halfLength));
        }
    }

    #endregion

    #region 摆标签、差值、箭头

    /// <summary>
    /// 把标签、差值、双向箭头摆到位（界面坐标 = ScottPlot 像素 ÷ 显示缩放）。重画时摆一次，图真正画完再对一次。
    /// </summary>
    private void PlaceReferenceOverlays()
    {
        var rect = _plot.LastRender.DataRect;
        bool ready = rect.Width > 0 && rect.Height > 0;
        double scale = PixelScale();
        if (ready && ShowXLines)
        {
            PlaceXOverlays(rect, scale);
        }
        else
        {
            ShowOverlay(XDelta, false);
            ShowOverlay(XDeltaArrow, false);
        }

        if (ready && ShowYLines)
        {
            PlaceYOverlays(rect, scale);
        }
        else
        {
            ShowOverlay(YDelta, false);
            ShowOverlay(YDeltaArrow, false);
        }
    }

    /// <summary>
    /// X 线：标签贴在时间轴上（压住那一段的刻度字），挨着的各往外推；时间差标在绘图区顶上，
    /// 箭头只画看得见的那一段，另一条线不在眼前的那头不画箭尖；中间放不下就把差放到线外面。
    /// </summary>
    private void PlaceXOverlays(PixelRect rect, double scale)
    {
        double first = _plot.Axes.Bottom.GetPixel(_x1, rect) / scale;
        double second = _plot.Axes.Bottom.GetPixel(_x2, rect) / scale;
        double left = rect.Left / scale;
        double right = rect.Right / scale;
        double bottom = rect.Bottom / scale;

        var firstSize = MeasureOverlay(X1Tag);
        var secondSize = MeasureOverlay(X2Tag);
        var centers = Spread(first, second, (firstSize.Width + secondSize.Width) / 2 + TagSpacing);
        double maxLeft = Math.Max(0, Overlay.ActualWidth - firstSize.Width);
        PlaceOverlay(X1Tag, Math.Clamp(centers.First - firstSize.Width / 2, 0, maxLeft), bottom + 2);
        maxLeft = Math.Max(0, Overlay.ActualWidth - secondSize.Width);
        PlaceOverlay(X2Tag, Math.Clamp(centers.Second - secondSize.Width / 2, 0, maxLeft), bottom + 2);

        double from = Math.Max(left, Math.Min(first, second));
        double to = Math.Min(right, Math.Max(first, second));
        if (double.IsNaN(from) || double.IsNaN(to) || to < from)
        {
            ShowOverlay(XDelta, false);
            ShowOverlay(XDeltaArrow, false);
            return;
        }

        double y = rect.Top / scale + DeltaXInset;
        ShowOverlay(XDeltaArrow, to - from >= MinArrowLength);
        if (to - from >= MinArrowLength)
        {
            XDeltaArrow.Data = DeltaArrowGeometry(
                new WpfPoint(from, y), new WpfPoint(to, y), Math.Min(first, second) >= left, Math.Max(first, second) <= right);
        }

        ShowOverlay(XDelta, true);
        var size = MeasureOverlay(XDelta);
        double labelLeft = from - 8 - size.Width;
        if (to - from >= size.Width + DeltaRoom * 2)
        {
            labelLeft = (from + to - size.Width) / 2;
        }
        else if (to + 8 + size.Width <= right)
        {
            labelLeft = to + 8;
        }

        PlaceOverlay(XDelta, labelLeft, y - size.Height / 2);
    }

    /// <summary>
    /// Y 线：标签贴在纵轴上（右边对齐绘图区左边），挨着的各往外推；数值差的箭头竖在绘图区右边一点，
    /// 差标在箭头中间，两条线太近就放到上面一条的上方（放不下放到下面）。
    /// </summary>
    private void PlaceYOverlays(PixelRect rect, double scale)
    {
        double first = _valueAxis.GetPixel(_y1, rect) / scale;
        double second = _valueAxis.GetPixel(_y2, rect) / scale;
        double left = rect.Left / scale;
        double top = rect.Top / scale;
        double bottom = rect.Bottom / scale;

        var firstSize = MeasureOverlay(Y1Tag);
        var secondSize = MeasureOverlay(Y2Tag);
        var centers = Spread(first, second, (firstSize.Height + secondSize.Height) / 2 + TagSpacing);
        PlaceOverlay(Y1Tag, left - firstSize.Width, centers.First - firstSize.Height / 2);
        PlaceOverlay(Y2Tag, left - secondSize.Width, centers.Second - secondSize.Height / 2);

        double from = Math.Max(top, Math.Min(first, second));
        double to = Math.Min(bottom, Math.Max(first, second));
        if (double.IsNaN(from) || double.IsNaN(to) || to < from)
        {
            ShowOverlay(YDelta, false);
            ShowOverlay(YDeltaArrow, false);
            return;
        }

        double x = rect.Right / scale - DeltaYInset;
        ShowOverlay(YDeltaArrow, to - from >= MinArrowLength);
        if (to - from >= MinArrowLength)
        {
            YDeltaArrow.Data = DeltaArrowGeometry(
                new WpfPoint(x, from), new WpfPoint(x, to), Math.Min(first, second) >= top, Math.Max(first, second) <= bottom);
        }

        ShowOverlay(YDelta, true);
        var size = MeasureOverlay(YDelta);
        double centerY = to + 4 + size.Height / 2;
        if (to - from >= size.Height + DeltaRoom * 2)
        {
            centerY = (from + to) / 2;
        }
        else if (from - 4 - size.Height >= top)
        {
            centerY = from - 4 - size.Height / 2;
        }

        PlaceOverlay(YDelta, x - size.Width / 2, centerY - size.Height / 2);
    }

    /// <summary>
    /// 双向箭头：一根线，看得见的那一端画箭尖（另一条线不在眼前的那头不画）。
    /// </summary>
    private static Geometry DeltaArrowGeometry(WpfPoint from, WpfPoint to, bool headAtFrom, bool headAtTo)
    {
        var direction = to - from;
        direction.Normalize();
        var normal = new WpfVector(-direction.Y, direction.X);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(from, false, false);
            context.LineTo(to, true, false);
            if (headAtFrom)
            {
                var back = from + direction * ArrowLength;
                context.BeginFigure(back + normal * ArrowHalfWidth, false, false);
                context.LineTo(from, true, false);
                context.LineTo(back - normal * ArrowHalfWidth, true, false);
            }

            if (headAtTo)
            {
                var back = to - direction * ArrowLength;
                context.BeginFigure(back + normal * ArrowHalfWidth, false, false);
                context.LineTo(to, true, false);
                context.LineTo(back - normal * ArrowHalfWidth, true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// 图真正画完一帧：绘图区可能挪了（纵轴刻度变宽、窗口大小变了），标签、差值再按这一帧对一次位置。
    /// </summary>
    private void OnPlotRendered(object? sender, RenderDetails details)
    {
        if (!ShowXLines && !ShowYLines)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, PlaceReferenceOverlays);
    }

    private static void ShowOverlay(UIElement element, bool show)
    {
        element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void PlaceOverlay(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
    }

    /// <summary>
    /// 量标签现在的大小：字刚改过时框本身还算"量过"，直接量会拿回上一次的大小，先让它作废再量。
    /// </summary>
    private static WpfSize MeasureOverlay(UIElement element)
    {
        element.InvalidateMeasure();
        element.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    }

    /// <summary>
    /// 两个标签的中心挨得比 size 近，就以两者中点为中心各往外推开到 size。
    /// </summary>
    private static (double First, double Second) Spread(double first, double second, double size)
    {
        if (Math.Abs(first - second) >= size)
        {
            return (first, second);
        }

        double middle = (first + second) / 2;
        if (first <= second)
        {
            return (middle - size / 2, middle + size / 2);
        }

        return (middle + size / 2, middle - size / 2);
    }

    #endregion

    #region 鼠标：指着线、拖线

    /// <summary>
    /// 鼠标下面是哪条基准线：先看轴上的标签，再看离线 6 像素以内最近的那条（只在绘图区里算）。
    /// </summary>
    private TrendReferenceLine LineAt(Pixel pixel)
    {
        var rect = _plot.LastRender.DataRect;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return TrendReferenceLine.None;
        }

        double scale = PixelScale();
        var point = new WpfPoint(pixel.X / scale, pixel.Y / scale);
        if (TagContains(X1Tag, point))
        {
            return TrendReferenceLine.X1;
        }

        if (TagContains(X2Tag, point))
        {
            return TrendReferenceLine.X2;
        }

        if (TagContains(Y1Tag, point))
        {
            return TrendReferenceLine.Y1;
        }

        if (TagContains(Y2Tag, point))
        {
            return TrendReferenceLine.Y2;
        }

        if (!rect.Contains(pixel.X, pixel.Y))
        {
            return TrendReferenceLine.None;
        }

        var best = TrendReferenceLine.None;
        double bestDistance = GrabPixels * scale;
        if (ShowXLines)
        {
            Closer(ref best, ref bestDistance, TrendReferenceLine.X1, Math.Abs(pixel.X - _plot.Axes.Bottom.GetPixel(_x1, rect)));
            Closer(ref best, ref bestDistance, TrendReferenceLine.X2, Math.Abs(pixel.X - _plot.Axes.Bottom.GetPixel(_x2, rect)));
        }

        if (ShowYLines)
        {
            Closer(ref best, ref bestDistance, TrendReferenceLine.Y1, Math.Abs(pixel.Y - _valueAxis.GetPixel(_y1, rect)));
            Closer(ref best, ref bestDistance, TrendReferenceLine.Y2, Math.Abs(pixel.Y - _valueAxis.GetPixel(_y2, rect)));
        }

        return best;
    }

    private static void Closer(ref TrendReferenceLine best, ref double bestDistance, TrendReferenceLine line, double distance)
    {
        if (distance <= bestDistance)
        {
            best = line;
            bestDistance = distance;
        }
    }

    private static bool TagContains(FrameworkElement tag, WpfPoint point)
    {
        if (tag.Visibility != Visibility.Visible)
        {
            return false;
        }

        double left = Canvas.GetLeft(tag);
        double top = Canvas.GetTop(tag);
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return false;
        }

        return point.X >= left - 2 && point.X <= left + tag.ActualWidth + 2
               && point.Y >= top - 2 && point.Y <= top + tag.ActualHeight + 2;
    }

    /// <summary>
    /// 鼠标移动（没在拖）：指着基准线就换成左右 / 上下箭头、那条线加粗，返回 true（这时不弹悬停数值框）。
    /// </summary>
    private bool UpdateLineUnderMouse(Pixel pixel)
    {
        var line = LineAt(pixel);
        if (line != _lineUnderMouse)
        {
            _lineUnderMouse = line;
            PlotView.Cursor = CursorFor(line);
            RequestRender();
        }

        return line != TrendReferenceLine.None;
    }

    /// <summary>
    /// 鼠标离开图：指着的线不再算指着（拖着的时候不管）。
    /// </summary>
    private void ClearLineUnderMouse()
    {
        if (_lineDragging != TrendReferenceLine.None || _lineUnderMouse == TrendReferenceLine.None)
        {
            return;
        }

        _lineUnderMouse = TrendReferenceLine.None;
        PlotView.Cursor = Cursors.Cross;
        RequestRender();
    }

    private static WpfCursor CursorFor(TrendReferenceLine line)
    {
        switch (line)
        {
            case TrendReferenceLine.X1:
            case TrendReferenceLine.X2:
                return Cursors.SizeWE;
            case TrendReferenceLine.Y1:
            case TrendReferenceLine.Y2:
                return Cursors.SizeNS;
            default:
                return Cursors.Cross;
        }
    }

    /// <summary>
    /// 左键按下：按在基准线（或它的标签）上就开始拖这条线，返回 true；没按在线上返回 false，照旧平移。
    /// </summary>
    private bool StartLineDrag(Pixel pixel)
    {
        var line = LineAt(pixel);
        if (line == TrendReferenceLine.None)
        {
            return false;
        }

        HideHover();
        _lineDragging = line;
        _lineUnderMouse = line;
        PlotView.Cursor = CursorFor(line);
        PlotView.CaptureMouse();
        RequestRender();
        return true;
    }

    /// <summary>
    /// 拖着走：X 线对准最近的采样点（附近没有就跟着鼠标），Y 线取到纵轴范围的 1/500 那一位；都不出当前看到的范围。
    /// </summary>
    private void DragLine(Pixel pixel)
    {
        var limits = _plot.Axes.GetLimits();
        var coordinates = _plot.GetCoordinates(pixel);
        double time = Math.Clamp(coordinates.X, limits.Left, limits.Right);
        double value = Math.Clamp(coordinates.Y, limits.Bottom, limits.Top);
        switch (_lineDragging)
        {
            case TrendReferenceLine.X1:
                _x1 = SnapTime(CurrentSeries(), time);
                break;
            case TrendReferenceLine.X2:
                _x2 = SnapTime(CurrentSeries(), time);
                break;
            case TrendReferenceLine.Y1:
                _y1 = RoundValue(value, limits.Top - limits.Bottom);
                break;
            case TrendReferenceLine.Y2:
                _y2 = RoundValue(value, limits.Top - limits.Bottom);
                break;
            default:
                return;
        }

        RequestRender();
    }

    /// <summary>
    /// 松开左键：放下手上的线（停在拖到的地方）。
    /// </summary>
    private void EndLineDrag()
    {
        _lineDragging = TrendReferenceLine.None;
        PlotView.ReleaseMouseCapture();
        RequestRender();
    }

    #endregion

    #region 换算、写法

    private double PixelScale()
    {
        return PlotView.DisplayScale > 0 ? PlotView.DisplayScale : 1;
    }

    private static bool Within(double value, double from, double to)
    {
        return !double.IsNaN(value) && value >= from && value <= to;
    }

    /// <summary>
    /// X 线对准最近的采样时刻（离得远就不对，停在原处）。
    /// </summary>
    private double SnapTime(List<TrendSeries> series, double x)
    {
        double? snapped = SnapToSample(series, x);
        return snapped ?? x;
    }

    /// <summary>
    /// Y 线的值取到纵轴范围的 1/500 那一位（范围 0~200 取到 0.1），标签上不出一长串小数。
    /// </summary>
    private static double RoundValue(double value, double span)
    {
        if (span <= 0)
        {
            return value;
        }

        double step = Math.Pow(10, Math.Floor(Math.Log10(span / ValueResolutionParts)));
        return Math.Round(value / step) * step;
    }

    /// <summary>
    /// X 标签上的时刻：看的时间段超过一天带上日期。
    /// </summary>
    private string FormatTagTime(double x)
    {
        if (double.IsNaN(x))
        {
            return string.Empty;
        }

        var time = DateTime.FromOADate(x);
        return _timeSpanDays > 1
            ? time.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatValue(double value)
    {
        return double.IsNaN(value) ? string.Empty : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 时间差（天）写成"5 分 10 秒"，按秒取整。
    /// </summary>
    private static string FormatDuration(double days)
    {
        if (double.IsNaN(days))
        {
            return string.Empty;
        }

        long seconds = (long)Math.Round(Math.Abs(days) * 86400);
        if (seconds < 60)
        {
            return L10n.Get("chart.duration_s", seconds);
        }

        long minutes = seconds / 60;
        if (minutes < 60)
        {
            return L10n.Get("chart.duration_ms", minutes, seconds % 60);
        }

        return L10n.Get("chart.duration_hms", minutes / 60, minutes % 60, seconds % 60);
    }

    #endregion
}
