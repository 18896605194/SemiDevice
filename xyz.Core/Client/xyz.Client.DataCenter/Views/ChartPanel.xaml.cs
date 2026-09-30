using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 曲线 + 曲线信息面板：数据曲线、实时曲线两页共用（绑页面 ViewModel 的 Series、RangeStart/RangeEnd、FollowRange、
/// VisibleStart/VisibleEnd、CursorTime）。右上角"放大"把整块（同一个图、同一张表，缩放状态、勾选都不变）搬进单独的窗口全屏显示，
/// 窗口里再点一下、按 Esc 或关掉窗口就搬回来；搬出去期间页面这儿显示提示和"收回"。
/// </summary>
public partial class ChartPanel : UserControl
{
    public static readonly DependencyProperty ValueHeaderProperty = DependencyProperty.Register(
        nameof(ValueHeader), typeof(string), typeof(ChartPanel),
        new PropertyMetadata(string.Empty, (d, e) => ((ChartPanel)d).InfoTable.ValueHeader = (string)e.NewValue));

    public static readonly DependencyProperty WindowTitleProperty = DependencyProperty.Register(
        nameof(WindowTitle), typeof(string), typeof(ChartPanel), new PropertyMetadata(string.Empty));

    private ChartWindow? _window;

    public ChartPanel()
    {
        InitializeComponent();
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
