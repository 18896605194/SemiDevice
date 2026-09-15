using System.Windows.Controls;
using xyz._35021.Client.Manual.ViewModels;
using xyz.Tools;

namespace xyz._35021.Client.Manual.Views;

/// <summary>
/// 35021 Transfer 调度界面：搬运地图（站点 + 机械手）与机械手手动取放片。
/// </summary>
public partial class TransferView : UserControl
{
    public TransferView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<TransferViewModel>();
    }
}
