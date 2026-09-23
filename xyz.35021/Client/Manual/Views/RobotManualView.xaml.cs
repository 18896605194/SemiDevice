using System.Windows.Controls;
using xyz._35021.Client.Manual.ViewModels;
using xyz.Tools;

namespace xyz._35021.Client.Manual.Views;

/// <summary>
/// 35021 Robot 手动界面：按后端配置的 Robot 数量，组装多个平台手动操作面板。
/// </summary>
public partial class RobotManualView : UserControl
{
    public RobotManualView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<RobotManualViewModel>();
    }
}
