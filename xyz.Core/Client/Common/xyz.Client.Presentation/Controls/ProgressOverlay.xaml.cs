using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 进度弹框（查询、导出……都用它）：盖住整页、中间一个圆环，开关、进度、取消绑 VM 上的 ProgressState
/// </summary>
public partial class ProgressOverlay : UserControl
{
    /// <summary>
    /// 圆环外径，跟 XAML 里那个 Grid 一样大
    /// </summary>
    private const double RingSize = 136;

    /// <summary>
    /// 圆环粗细，跟 XAML 里的 StrokeThickness 一样
    /// </summary>
    private const double RingStroke = 10;

    /// <summary>
    /// 不知道进度时转着的那段弧有多长（度）
    /// </summary>
    private const double SpinArc = 100;

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(ProgressState), typeof(ProgressOverlay),
            new PropertyMetadata(null, (d, e) => ((ProgressOverlay)d).OnStateChanged((ProgressState?)e.OldValue, (ProgressState?)e.NewValue)));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ProgressOverlay), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(ProgressOverlay),
            new PropertyMetadata(false, (d, _) => ((ProgressOverlay)d).UpdateRing()));

    public static readonly DependencyProperty ShowDelayProperty =
        DependencyProperty.Register(nameof(ShowDelay), typeof(int), typeof(ProgressOverlay), new PropertyMetadata(500));

    /// <summary>
    /// 开始以后等 ShowDelay 还没完才弹，快的不弹
    /// </summary>
    private readonly DispatcherTimer _delay = new();

    /// <summary>
    /// 不知道进度时一秒转一圈
    /// </summary>
    private readonly DoubleAnimation _spinAnimation = new(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };

    private bool _spinning;

    public ProgressOverlay()
    {
        InitializeComponent();
        _delay.Tick += OnDelayTick;
        UpdateRing();
    }

    /// <summary>
    /// 绑 VM 上的 ProgressState：开没开、进度、取消都从它来
    /// </summary>
    public ProgressState? State
    {
        get => (ProgressState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>
    /// 圆环下面那行字（页面上绑语言包，如 {DynamicResource datachart.exporting}）
    /// </summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// 不知道进度（查询）：圆环一直转，中间不写百分比
    /// </summary>
    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    /// <summary>
    /// 开始以后过多少毫秒还没完才弹出来（默认 500）：放大、拖动时的快查询不闪
    /// </summary>
    public int ShowDelay
    {
        get => (int)GetValue(ShowDelayProperty);
        set => SetValue(ShowDelayProperty, value);
    }

    private void OnStateChanged(ProgressState? old, ProgressState? state)
    {
        if (old is not null)
        {
            old.PropertyChanged -= OnStatePropertyChanged;
        }

        if (state is not null)
        {
            state.PropertyChanged += OnStatePropertyChanged;
        }

        UpdateOpen();
        UpdateRing();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnStatePropertyChanged(sender, e));
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ProgressState.IsOpen):
                UpdateOpen();
                break;
            case nameof(ProgressState.Value):
                UpdateRing();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// 开了：等 ShowDelay 还没关再弹；关了：马上收起
    /// </summary>
    private void UpdateOpen()
    {
        if (State?.IsOpen != true)
        {
            _delay.Stop();
            Visibility = Visibility.Collapsed;
            UpdateSpin();
            return;
        }

        if (Visibility == Visibility.Visible || _delay.IsEnabled)
        {
            return;
        }

        if (ShowDelay <= 0)
        {
            Show();
            return;
        }

        _delay.Interval = TimeSpan.FromMilliseconds(ShowDelay);
        _delay.Start();
    }

    private void OnDelayTick(object? sender, EventArgs e)
    {
        _delay.Stop();
        if (State?.IsOpen == true)
        {
            Show();
        }
    }

    private void Show()
    {
        UpdateRing();
        Visibility = Visibility.Visible;
        UpdateSpin();
    }

    /// <summary>
    /// 圆环和百分比：知道进度的按 Value 画弧、写百分比；不知道的画一段弧、转着
    /// </summary>
    private void UpdateRing()
    {
        if (IsIndeterminate)
        {
            Arc.Data = ArcGeometry(-90, SpinArc);
            PercentText.Visibility = Visibility.Collapsed;
            UpdateSpin();
            return;
        }

        double value = State?.Value ?? 0;
        Arc.Data = ArcGeometry(-90, value / 100 * 360);
        PercentText.Text = $"{value:0}%";
        PercentText.Visibility = Visibility.Visible;
        UpdateSpin();
    }

    /// <summary>
    /// 只在看得见、又不知道进度时转，收起来就停，不白占界面线程
    /// </summary>
    private void UpdateSpin()
    {
        bool spin = IsIndeterminate && Visibility == Visibility.Visible;
        if (spin == _spinning)
        {
            return;
        }

        _spinning = spin;
        if (spin)
        {
            Spin.BeginAnimation(RotateTransform.AngleProperty, _spinAnimation);
            return;
        }

        Spin.BeginAnimation(RotateTransform.AngleProperty, null);
        Spin.Angle = 0;
    }

    /// <summary>
    /// 从 start 度（-90 = 正上方）顺时针画 sweep 度的弧，画在圆环粗细的正中
    /// </summary>
    private static Geometry ArcGeometry(double start, double sweep)
    {
        if (sweep <= 0)
        {
            return Geometry.Empty;
        }

        double radius = (RingSize - RingStroke) / 2;
        var center = new Point(RingSize / 2, RingSize / 2);
        if (sweep >= 360)
        {
            return new EllipseGeometry(center, radius, radius);
        }

        var figure = new PathFigure { StartPoint = PointAt(center, radius, start), IsClosed = false };
        figure.Segments.Add(new ArcSegment(PointAt(center, radius, start + sweep), new Size(radius, radius), 0, sweep > 180,
            SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
