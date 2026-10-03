using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 日期时间选择：日期框 + "时"下拉（00~23）+ "分"下拉（00~59），精确到分钟。报警历史、日志历史、数据曲线的查询时间段都用它。
/// 绑 Value（双向）；日期清空时 Value 为 null，由页面按自己的默认值处理。
/// </summary>
public partial class DateTimePicker : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(DateTime?), typeof(DateTimePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((DateTimePicker)d).ShowValue()));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(DateTimePicker), new PropertyMetadata(string.Empty));

    /// <summary>
    /// 下拉里的选项：下标就是几点、几分。
    /// </summary>
    private static readonly string[] Hours = Enumerable.Range(0, 24).Select(hour => hour.ToString("00")).ToArray();

    private static readonly string[] Minutes = Enumerable.Range(0, 60).Select(minute => minute.ToString("00")).ToArray();

    /// <summary>
    /// 界面把 Value 拆到三个框里时置位，免得框的变化事件又反过来改 Value。
    /// </summary>
    private bool _showing;

    public DateTimePicker()
    {
        InitializeComponent();
        HourPart.ItemsSource = Hours;
        MinutePart.ItemsSource = Minutes;
    }

    /// <summary>
    /// 选中的时刻（秒一律为 0）。
    /// </summary>
    public DateTime? Value
    {
        get => (DateTime?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// 提示文字（开始时间、结束时间……），显示在日期框上。
    /// </summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    private void ShowValue()
    {
        _showing = true;
        try
        {
            DatePart.SelectedDate = Value?.Date;
            HourPart.SelectedIndex = Value?.Hour ?? -1;
            MinutePart.SelectedIndex = Value?.Minute ?? -1;
        }
        finally
        {
            _showing = false;
        }
    }

    private void OnDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        Compose();
    }

    private void OnTimeChanged(object sender, SelectionChangedEventArgs e)
    {
        Compose();
    }

    /// <summary>
    /// 三个框合成 Value：日期取日期框，时、分取两个下拉（没选的按 0）。
    /// </summary>
    private void Compose()
    {
        if (_showing)
        {
            return;
        }

        var date = DatePart.SelectedDate;
        if (date is null)
        {
            SetCurrentValue(ValueProperty, null);
            return;
        }

        var value = date.Value.Date
            .AddHours(Math.Max(0, HourPart.SelectedIndex))
            .AddMinutes(Math.Max(0, MinutePart.SelectedIndex));
        if (Value != value)
        {
            SetCurrentValue(ValueProperty, value);
        }
    }
}
