using System.Windows.Controls;
using xyz.Client.Main.ViewModels;
using xyz.Client.Modules;
using xyz.Tools;

namespace xyz.Client.Main.Views;

/// <summary>
/// 主界面：左 系统操作、中 整机调度、右 LoadPort 页签。
/// 中间那一块不写死在这儿：按 ClientViewKeys.MainDispatch 从 DI 取——平台注册了默认的（DispatchView，按机械手站点表自动摆），
/// 机型在自己的 IClientModule.Register 里用同一个键注册别的，就换成机型的，主界面别的地方不用动。
/// </summary>
public partial class MainPageView : UserControl
{
    public MainPageView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<MainPageViewModel>();
        DispatchHost.Content = IocHelper.GetRequiredKeyedService<UserControl>(ClientViewKeys.MainDispatch);
    }
}
