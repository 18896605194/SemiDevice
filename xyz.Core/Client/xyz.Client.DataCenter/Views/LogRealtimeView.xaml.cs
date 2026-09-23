using System.Windows.Controls;
using xyz.Client.DataCenter.ViewModels;
using xyz.Tools;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 实时日志页。
/// </summary>
public partial class LogRealtimeView : UserControl
{
    public LogRealtimeView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<LogRealtimeViewModel>();
    }
}
