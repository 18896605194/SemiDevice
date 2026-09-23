using System.Windows.Controls;
using xyz._35021.Client.Manual.ViewModels;
using xyz.Tools;

namespace xyz._35021.Client.Manual.Views;

/// <summary>
/// 35021 Transfer 调度界面：显示机械手（去哪、朝哪、手臂上的片）。
/// </summary>
public partial class TransferView : UserControl
{
    public TransferView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<TransferViewModel>();
    }
}
