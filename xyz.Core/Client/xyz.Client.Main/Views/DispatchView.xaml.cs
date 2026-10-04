using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.Main.Views;

/// <summary>
/// 平台默认的整机调度（主界面中间那一块）：照 sc.xml 装了的机械手，一台一张调度图（DispatchMap 自己订推送），
/// 站点按各自站点表里的方向摆。机械手名单启动时从后端系统设置拿一次（跟 IO、腔体手动页一样），由注册的地方传进来。
/// </summary>
public partial class DispatchView : UserControl
{
    public DispatchView(IReadOnlyList<string> robots)
    {
        InitializeComponent();
        RobotList.ItemsSource = robots;

        // 名单是启动时定的，不会变：一个都没有就只显示提示
        EmptyHint.Visibility = robots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
