using System.Windows.Controls;
using xyz.Client.DataCenter.ViewModels;
using xyz.Tools;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// Job 历史页
/// </summary>
public partial class JobHistoryView : UserControl
{
    public JobHistoryView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<JobHistoryViewModel>();
    }
}
