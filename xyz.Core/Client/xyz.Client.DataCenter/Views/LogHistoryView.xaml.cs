using System.Windows.Controls;
using xyz.Client.DataCenter.ViewModels;
using xyz.Tools;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 日志历史页。
/// </summary>
public partial class LogHistoryView : UserControl
{
    public LogHistoryView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<LogHistoryViewModel>();
    }
}
