using System.Windows.Controls;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 信号勾选树面板：数据曲线、实时曲线两页共用，用所在页面的 DataContext（Signals、SearchText、Series、MaxSignals 和两个命令）。
/// </summary>
public partial class SignalTreePanel : UserControl
{
    public SignalTreePanel()
    {
        InitializeComponent();
    }
}
