using System.Windows.Controls;
using xyz.Client.Presentation.ViewModels;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 顶栏日志下拉框：显示后端日志（经事件流）与客户端自身报错。
/// 控件自带 ViewModel，放到任意窗口即可用。
/// </summary>
public partial class LogBar : UserControl
{
    public LogBar()
    {
        InitializeComponent();

        ViewModel = new LogViewModel();
        DataContext = ViewModel;
        ViewModel.Init();
    }

    /// <summary>
    /// 日志 ViewModel（消费者）。
    /// </summary>
    public LogViewModel ViewModel { get; }
}
