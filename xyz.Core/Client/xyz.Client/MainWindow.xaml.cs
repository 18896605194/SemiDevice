using Microsoft.Extensions.DependencyInjection;
using System.Windows;
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
}
