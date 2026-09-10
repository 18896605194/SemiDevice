using System.Windows;

namespace xyz.Client.Views;

/// <summary>
/// 启动加载界面：后端连接、机型菜单、ViewModel 初始化期间显示，
/// 主界面准备好后由 App 关闭本窗口。
/// </summary>
public partial class LoadingWindow : Window
{
    public LoadingWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 更新加载进度（0-100）和当前步骤文案。
    /// </summary>
    public void Report(int percent, string message)
    {
        ProgressBar.Value = Math.Clamp(percent, 0, 100);
        ProgressText.Text = $"{ProgressBar.Value:0}%";
        StatusText.Text = message;
    }
}
