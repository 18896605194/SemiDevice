using System.Windows.Controls;
using xyz.Client.Alarm.ViewModels;
using xyz.Tools;

namespace xyz.Client.Alarm.Views;

/// <summary>
/// 实时报警页。
/// </summary>
public partial class AlarmRealtimeView : UserControl
{
    public AlarmRealtimeView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<AlarmRealtimeViewModel>();
    }
}
