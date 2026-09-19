using System.Windows.Controls;
using xyz.Client.DataCenter.ViewModels;
using xyz.Tools;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 报警历史页。
/// </summary>
public partial class AlarmHistoryView : UserControl
{
    public AlarmHistoryView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<AlarmHistoryViewModel>();
    }
}
