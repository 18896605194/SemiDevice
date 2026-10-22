using System.Windows.Controls;
using xyz.Client.Presentation.ViewModels;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 顶栏模块行：切哪一页都看得到各模块的状态和 Auto / Manual。控件自带 ViewModel
/// </summary>
public partial class ModuleStatusBar : UserControl
{
    public ModuleStatusBar()
    {
        InitializeComponent();

        ViewModel = new ModuleStatusBarViewModel();
        DataContext = ViewModel;
        ViewModel.Init();
    }

    /// <summary>
    /// 模块行 ViewModel
    /// </summary>
    public ModuleStatusBarViewModel ViewModel { get; }
}
