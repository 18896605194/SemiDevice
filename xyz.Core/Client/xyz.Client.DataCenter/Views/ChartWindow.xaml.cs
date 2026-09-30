using System.Windows;
using System.Windows.Input;
using xyz.Client.Presentation.Helpers;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 曲线放大窗口：打开时最大化，把页面上的曲线面板（ChartPanel 的那一整块）接过来显示；按 Esc 关窗口。
/// 关窗口时由 ChartPanel 调 Release 把那一块交还，再放回页面。
/// </summary>
public partial class ChartWindow : Window
{
    public ChartWindow(UIElement content)
    {
        InitializeComponent();
        Host.Child = content;
    }

    /// <summary>
    /// 把接过来的那一块交还（放回页面之前先从这儿拿掉，一个控件不能同时有两个父元素）。
    /// </summary>
    public void Release()
    {
        Host.Child = null;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
