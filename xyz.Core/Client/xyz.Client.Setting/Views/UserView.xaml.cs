using System.Windows.Controls;
using xyz.Tools;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Setting.ViewModels;

namespace xyz.Client.Setting.Views;

public partial class UserView : UserControl
{
    public UserView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<UserViewModel>();
    }
}
