using System.Windows.Controls;
using xyz.Client.DataCenter.ViewModels;
using xyz.Tools;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 数据曲线页：按时间段查后端每秒入库的数据，勾哪些信号画哪些。
/// </summary>
public partial class DataChartView : UserControl
{
    public DataChartView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<DataChartViewModel>();
    }
}
