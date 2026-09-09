using System.Windows.Controls;
using xyz.Tools;
using xyz.Client.Io.ViewModels;

namespace xyz.Client.Io.Views;

public partial class IoView : UserControl
{
    public IoView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<IoViewModel>();
    }
}
