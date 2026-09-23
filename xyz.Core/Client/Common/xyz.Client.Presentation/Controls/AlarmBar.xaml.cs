using System.Windows.Controls;
using xyz.Client.Presentation.ViewModels;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 顶栏报警栏：当前报警的条数和最新一条，展开看全部。控件自带 ViewModel，放到任意窗口即可用。
/// </summary>
public partial class AlarmBar : UserControl
{
    public AlarmBar()
    {
        InitializeComponent();

        ViewModel = new AlarmBarViewModel();
        DataContext = ViewModel;
        ViewModel.Init();
    }

    /// <summary>
    /// 报警栏 ViewModel。
    /// </summary>
    public AlarmBarViewModel ViewModel { get; }
}
