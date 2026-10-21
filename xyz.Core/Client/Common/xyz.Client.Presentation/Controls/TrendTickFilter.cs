using ScottPlot;
using ScottPlot.TickGenerators;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 趋势图坐标轴的刻度：包着原来的刻度生成器，被基准线标签压住的刻度只留刻度线和网格、不写字，
/// 标签旁边不露半截刻度字（"10:22:00" 被"X1 10:21:50"盖掉一半）。
/// 时间轴只认日期类的刻度生成器，所以按日期类的接口包；纵轴也用它，换算日期那一步用不上。
/// </summary>
public sealed class TrendTickFilter : IDateTimeTickGenerator
{
    /// <summary>
    /// 刻度字跟标签之间至少留这么多像素。
    /// </summary>
    private const double Gap = 3;

    private Tick[] _ticks = [];

    public TrendTickFilter(ITickGenerator inner)
    {
        Inner = inner;
    }

    /// <summary>
    /// 真正出刻度的那个（纵轴在"自动疏密"和"只标 0、1"之间换）。
    /// </summary>
    public ITickGenerator Inner { get; set; }

    /// <summary>
    /// 标签占着的地方，一个标签一项：标签对着的坐标；标签中心离那个坐标几个像素（往坐标变大的方向为正，
    /// 两个标签挨得近时各往外推）；标签顺着轴方向的一半长（像素）。
    /// </summary>
    public IReadOnlyList<(double Position, double Offset, double HalfLength)> Blocks { get; set; } = [];

    public Tick[] Ticks => _ticks;

    public int MaxTickCount
    {
        get => Inner.MaxTickCount;
        set => Inner.MaxTickCount = value;
    }

    public IEnumerable<double> ConvertToCoordinateSpace(IEnumerable<DateTime> dates)
    {
        if (Inner is IDateTimeTickGenerator dateTicks)
        {
            return dateTicks.ConvertToCoordinateSpace(dates);
        }

        return dates.Select(date => date.ToOADate());
    }

    /// <summary>
    /// 先让原来的生成器出刻度，再把字会碰到标签的那几个刻度换成不写字的。
    /// </summary>
    public void Regenerate(CoordinateRange range, Edge edge, PixelLength size, Paint paint, LabelStyle labelStyle)
    {
        Inner.Regenerate(range, edge, size, paint, labelStyle);
        var ticks = Inner.Ticks;
        double span = range.Max - range.Min;
        if (Blocks.Count == 0 || span <= 0 || size.Length <= 0)
        {
            _ticks = ticks;
            return;
        }

        bool vertical = edge == Edge.Left || edge == Edge.Right;
        double pixelsPerUnit = size.Length / span;
        var kept = new Tick[ticks.Length];
        for (int index = 0; index < ticks.Length; index++)
        {
            var tick = ticks[index];
            kept[index] = tick;
            if (!tick.IsMajor || string.IsNullOrEmpty(tick.Label))
            {
                continue;
            }

            var measured = labelStyle.Measure(tick.Label, paint);
            double half = (vertical ? measured.Height : measured.Width) / 2;
            double center = (tick.Position - range.Min) * pixelsPerUnit;
            foreach (var block in Blocks)
            {
                double blockCenter = (block.Position - range.Min) * pixelsPerUnit + block.Offset;
                if (Math.Abs(center - blockCenter) < half + block.HalfLength + Gap)
                {
                    kept[index] = new Tick(tick.Position, string.Empty, true);
                    break;
                }
            }
        }

        _ticks = kept;
    }
}
