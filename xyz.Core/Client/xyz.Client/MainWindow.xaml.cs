using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using xyz.Client.Common.Session;
using xyz.Client.Presentation.Helpers;
using xyz.Client.ViewModels;

namespace xyz.Client;

/// <summary>
/// 主窗口交互逻辑。标题栏和最小化 / 最大化 / 关闭都用系统自带的，只把标题栏设成深色。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContext = App.Services.GetRequiredService<MainViewModel>();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    /// <summary>
    /// 点顶栏的用户块：在它下面弹小菜单（登录 / 换人登录 / 改密码）；没启用登录不弹
    /// </summary>
    private void OnUserClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var menu = button.ContextMenu;
        if (!ClientSession.IsLoginEnabled || menu is null)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
