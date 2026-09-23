using System.Windows.Controls;
using xyz.Client.Io.ViewModels;

namespace xyz.Client.Io.Views;

/// <summary>
/// 一个模块的 IO 页面。模块名由注册时传进来的 ViewModel 决定，一个模块一个实例。
/// </summary>
public partial class IoView : UserControl
{
    public IoView(IoViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
