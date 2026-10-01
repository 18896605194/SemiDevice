using System.Windows.Controls;
using xyz.Client.Setting.ViewModels;
using xyz.Tools;

namespace xyz.Client.Setting.Views;

public partial class EcView : UserControl
{
    public EcView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<EcViewModel>();
    }
}
