using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 趋势图（TrendChart）上的一条曲线：数据 + 右下角信息表的一行。
/// 数据是两串等长的 List：Xs 是时间（OADate，升序），Ys 是值（NaN = 这一刻没数据，曲线在这儿断开）；
/// 图表按引用画这两串，改完调 <see cref="SetData"/> / <see cref="Append"/>，它们会发 <see cref="DataChanged"/> 让图表重画。
/// 颜色由图表控件加进来时按调色板分配。
/// </summary>
public sealed class TrendSeries : ObservableObject
{
    public TrendSeries(string name, string unit, bool isDigital, string description = "")
    {
        Name = name;
        Unit = unit;
        IsDigital = isDigital;
        Description = description;
    }

    /// <summary>
    /// 信号全名（Chamber1.Arm1.CurrentPosition）。
    /// </summary>
    public string Name { get; }

    public string Unit { get; }

    /// <summary>
    /// 开关量：值只有 0/1，图上画成阶梯线（跟模拟量同一根纵轴，就在 0 和 1 的位置）。
    /// </summary>
    public bool IsDigital { get; }

    public string Description { get; }

    public List<double> Xs { get; } = [];

    public List<double> Ys { get; } = [];

    /// <summary>
    /// 数据变了（整段换、追加）：图表据此重画。
    /// </summary>
    public event EventHandler? DataChanged;

    private Color _color = Colors.Transparent;

    public Color Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value))
            {
                var brush = new SolidColorBrush(value);
                brush.Freeze();
                Brush = brush;
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    /// <summary>
    /// 信息表里的色块。
    /// </summary>
    public Brush Brush { get; private set; } = Brushes.Transparent;

    private double? _cursorValue;

    /// <summary>
    /// 光标处的值（实时曲线没有光标时是最新值）。
    /// </summary>
    public double? CursorValue
    {
        get => _cursorValue;
        set => SetProperty(ref _cursorValue, value);
    }

    private double? _min;

    public double? Min
    {
        get => _min;
        set => SetProperty(ref _min, value);
    }

    private double? _max;

    public double? Max
    {
        get => _max;
        set => SetProperty(ref _max, value);
    }

    private double? _avg;

    public double? Avg
    {
        get => _avg;
        set => SetProperty(ref _avg, value);
    }

    /// <summary>
    /// 整段换数据（查询结果）。
    /// </summary>
    public void SetData(IEnumerable<double> xs, IEnumerable<double> ys)
    {
        Xs.Clear();
        Ys.Clear();
        Xs.AddRange(xs);
        Ys.AddRange(ys);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 追加一个点（实时曲线），比 keepFrom 早的点丢掉。
    /// </summary>
    public void Append(double x, double y, double keepFrom)
    {
        Xs.Add(x);
        Ys.Add(y);
        int stale = LowerBound(keepFrom);
        if (stale > 0)
        {
            Xs.RemoveRange(0, stale);
            Ys.RemoveRange(0, stale);
        }

        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// x 处的值：x 之前（含）最近的一个点——采样值保持到下一个采样；那个点没数据或 x 在第一个点之前给 null。
    /// </summary>
    public double? ValueAt(double x)
    {
        int index = UpperBound(x) - 1;
        if (index < 0 || double.IsNaN(Ys[index]))
        {
            return null;
        }

        return Ys[index];
    }

    /// <summary>
    /// 最新一个点的值；那一刻没数据给 null。
    /// </summary>
    public double? LastValue => Ys.Count > 0 && !double.IsNaN(Ys[^1]) ? Ys[^1] : null;

    /// <summary>
    /// [from, to] 之间有数据的点的最小、最大、平均值；一个都没有给 null。
    /// </summary>
    public (double? Min, double? Max, double? Avg) StatsBetween(double from, double to)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        double sum = 0;
        int count = 0;
        for (int index = LowerBound(from); index < Xs.Count && Xs[index] <= to; index++)
        {
            double value = Ys[index];
            if (double.IsNaN(value))
            {
                continue;
            }

            min = Math.Min(min, value);
            max = Math.Max(max, value);
            sum += value;
            count++;
        }

        return count == 0 ? (null, null, null) : (min, max, sum / count);
    }

    /// <summary>
    /// 离 x 最近的那个有数据的点的下标；一个点都没有给 -1。
    /// </summary>
    public int NearestIndex(double x)
    {
        int after = LowerBound(x);
        int before = after - 1;
        while (after < Xs.Count && double.IsNaN(Ys[after]))
        {
            after++;
        }

        while (before >= 0 && double.IsNaN(Ys[before]))
        {
            before--;
        }

        if (after >= Xs.Count)
        {
            return before;
        }

        return before >= 0 && x - Xs[before] <= Xs[after] - x ? before : after;
    }

    /// <summary>
    /// 第一个 Xs[i] &gt;= x 的下标。
    /// </summary>
    public int LowerBound(double x)
    {
        int low = 0;
        int high = Xs.Count;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (Xs[middle] < x)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// 第一个 Xs[i] &gt; x 的下标。
    /// </summary>
    public int UpperBound(double x)
    {
        int low = 0;
        int high = Xs.Count;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (Xs[middle] <= x)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
