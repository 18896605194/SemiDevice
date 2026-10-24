using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 曲线 + 曲线信息面板：数据曲线、实时曲线两页共用（绑页面 ViewModel 的 Series、RangeStart/RangeEnd、FollowRange、
/// VisibleStart/VisibleEnd、CursorTime）。右上角 X、Y 开关基准线（开着 X 时信息表多"X1 处 / X2 处"两列）；
/// "截图"把折线图那一块存成 PNG；"放大"把整块（同一个图、同一张表，缩放状态、勾选、基准线都不变）
/// 搬进单独的窗口全屏显示，窗口里再点一下、按 Esc 或关掉窗口就搬回来；搬出去期间页面这儿显示提示和"收回"。
/// </summary>
public partial class ChartPanel : UserControl
{
    public static readonly DependencyProperty ValueHeaderProperty = DependencyProperty.Register(
        nameof(ValueHeader), typeof(string), typeof(ChartPanel),
        new PropertyMetadata(string.Empty, (d, e) => ((ChartPanel)d).InfoTable.ValueHeader = (string)e.NewValue));

    public static readonly DependencyProperty WindowTitleProperty = DependencyProperty.Register(
        nameof(WindowTitle), typeof(string), typeof(ChartPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ScreenshotDirectoryProperty = DependencyProperty.Register(
        nameof(ScreenshotDirectory), typeof(string), typeof(ChartPanel), new PropertyMetadata(string.Empty));

    private ChartWindow? _window;

    /// <summary>
    /// 截图提示亮几秒就收起。
    /// </summary>
    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public ChartPanel()
    {
        InitializeComponent();
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            ScreenshotHint.Visibility = Visibility.Collapsed;
        };

        // 两个基准线开关、信息表的"X1 处 / X2 处"两列都跟着图走。绑定直接指着这张图、不按名字找：
        // 放大时整块搬进别的窗口，按名字找的绑定在那边找不到这张图。
        XLinesButton.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(TrendChart.ShowXLines)) { Source = Chart, Mode = BindingMode.TwoWay });
        YLinesButton.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(TrendChart.ShowYLines)) { Source = Chart, Mode = BindingMode.TwoWay });
        InfoTable.SetBinding(TrendInfoTable.ShowReferenceColumnsProperty, new Binding(nameof(TrendChart.ShowXLines)) { Source = Chart });
    }

    /// <summary>
    /// 截图存到哪个文件夹（页面 ViewModel 从后端 machine.xml 的 DataChart 节点拿的）；相对路径算客户端程序目录。
    /// </summary>
    public string ScreenshotDirectory
    {
        get => (string)GetValue(ScreenshotDirectoryProperty);
        set => SetValue(ScreenshotDirectoryProperty, value);
    }

    /// <summary>
    /// 信息表里值那一列的表头（"光标处" / "当前值"）。
    /// </summary>
    public string ValueHeader
    {
        get => (string)GetValue(ValueHeaderProperty);
        set => SetValue(ValueHeaderProperty, value);
    }

    /// <summary>
    /// 放大出去的窗口标题（页面名）。
    /// </summary>
    public string WindowTitle
    {
        get => (string)GetValue(WindowTitleProperty);
        set => SetValue(WindowTitleProperty, value);
    }

    /// <summary>
    /// 截图：只截折线图那一块（坐标轴、曲线；不带右上角的按钮、不带下面的信息表），存成 PNG，旁边提示几秒存到哪了。
    /// </summary>
    private void OnScreenshotClick(object sender, RoutedEventArgs e)
    {
        string message;
        if (string.IsNullOrWhiteSpace(ScreenshotDirectory))
        {
            message = L10n.Get("chart.screenshot_no_directory");
        }
        else
        {
            try
            {
                message = L10n.Get("chart.screenshot_saved", SaveScreenshot());
            }
            catch (Exception exception)
            {
                message = L10n.Get("chart.screenshot_failed", exception.Message);
            }
        }

        ScreenshotHintText.Text = message;
        ScreenshotHint.Visibility = Visibility.Visible;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    /// <summary>
    /// 按屏幕的缩放比例把折线图画成 PNG，存成"页面名_时间.png"，返回存的完整路径。
    /// </summary>
    private string SaveScreenshot()
    {
        var size = new Size(Chart.ActualWidth, Chart.ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(Chart);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX),
            (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        // 画到一张新画布上再截：直接截控件会把它在卡片里的偏移也带进去。图本身是透明底，先铺卡片的底色，存出来跟屏幕上看到的一样。
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var bounds = new Rect(size);
            var chart = new VisualBrush(Chart)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = bounds,
            };
            context.DrawRectangle(ChartCard.Background, null, bounds);
            context.DrawRectangle(chart, null, bounds);
        }

        bitmap.Render(visual);

        string directory = Path.GetFullPath(ScreenshotDirectory, AppContext.BaseDirectory);
        Directory.CreateDirectory(directory);
        string title = string.Concat(WindowTitle.Split(Path.GetInvalidFileNameChars()));
        string path = Path.Combine(directory, $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        return path;
    }

    private void OnPopOutClick(object sender, RoutedEventArgs e)
    {
        // 已经在窗口里了：这个按钮就是"收回"。
        if (_window is not null)
        {
            _window.Close();
            return;
        }

        // 搬家的空当里整块不挂在任何地方，继承来的 DataContext 会断一下，图就当成换了数据、回到整段——
        // 所以先把 ViewModel 钉在这一块上，缩放、框选的状态原样带过去。
        Body.DataContext = DataContext;
        Holder.Children.Remove(Body);
        Placeholder.Visibility = Visibility.Visible;
        PopOutIcon.Kind = PackIconKind.FullscreenExit;
        PopOutButton.SetResourceReference(ToolTipProperty, "chart.popin");

        _window = new ChartWindow(Body)
        {
            Owner = Window.GetWindow(this),
            DataContext = DataContext,
            Title = WindowTitle,
        };
        _window.Closed += OnWindowClosed;
        _window.Show();
    }

    private void OnPopInClick(object sender, RoutedEventArgs e)
    {
        _window?.Close();
    }

    /// <summary>
    /// 窗口关了（不管怎么关的）：整块搬回页面。
    /// </summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        _window.Closed -= OnWindowClosed;
        _window.Release();
        _window = null;

        Holder.Children.Add(Body);
        Body.ClearValue(DataContextProperty);
        Placeholder.Visibility = Visibility.Collapsed;
        PopOutIcon.Kind = PackIconKind.Fullscreen;
        PopOutButton.SetResourceReference(ToolTipProperty, "chart.popout");
    }
}
