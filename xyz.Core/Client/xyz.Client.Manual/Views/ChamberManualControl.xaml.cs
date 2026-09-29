using System.Windows;
using System.Windows.Controls;
using xyz.Client.Manual.ViewModels;

namespace xyz.Client.Manual.Views;

/// <summary>
/// 腔体手动操作面板：俯视图 + 状态色条 + 参数框 + 工艺 / Home / Abort / Reset 按钮。
/// 通过 ModuleName 依赖属性实例化，每个腔体一个实例（Manual 下一个腔体一个子菜单）。
/// </summary>
public partial class ChamberManualControl : UserControl
{
    public static readonly DependencyProperty ModuleNameProperty =
        DependencyProperty.Register(nameof(ModuleName), typeof(string), typeof(ChamberManualControl),
            new PropertyMetadata(string.Empty, OnModuleNameChanged));

    public ChamberManualControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 模块实例名（如 "Chamber1"），与 EventBus token / gRPC 参数一致。
    /// </summary>
    public string ModuleName
    {
        get => (string)GetValue(ModuleNameProperty);
        set => SetValue(ModuleNameProperty, value);
    }

    private static void OnModuleNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChamberManualControl control || e.NewValue is not string name || string.IsNullOrEmpty(name))
        {
            return;
        }

        // 只把 DataContext 挂在内容根节点上，不动控件自身的 DataContext：
        // 否则外层 <xxx ModuleName="{Binding}" /> 的绑定会因为 DataContext 变化二次求值，
        // 把 ViewModel 对象转成字符串再塞回 ModuleName。
        (control.Root.DataContext as ChamberManualViewModel)?.Dispose();

        var viewModel = new ChamberManualViewModel(name);
        control.Root.DataContext = viewModel;
        viewModel.Init();
    }
}
