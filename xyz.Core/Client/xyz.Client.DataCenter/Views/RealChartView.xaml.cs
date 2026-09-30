using System.Windows.Controls;
using xyz.Client.DataCenter.ViewModels;
using xyz.Tools;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 实时曲线页：后端每秒推一帧，勾哪些信号画哪些，横轴跟着最新时刻走。
/// </summary>
public partial class RealChartView : UserControl
{
    public RealChartView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<RealChartViewModel>();
    }
}
