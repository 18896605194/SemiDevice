using System.Windows.Controls;
using xyz.Client.Alarm.ViewModels;
using xyz.Tools;

namespace xyz.Client.Alarm.Views;

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
