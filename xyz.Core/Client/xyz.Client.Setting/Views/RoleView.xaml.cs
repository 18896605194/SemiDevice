using System.Windows.Controls;
using xyz.Client.DataModels.Ioc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Setting.ViewModels;

namespace xyz.Client.Setting.Views;

public partial class RoleView : UserControl
{
    public RoleView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<RoleViewModel>();
    }
}
