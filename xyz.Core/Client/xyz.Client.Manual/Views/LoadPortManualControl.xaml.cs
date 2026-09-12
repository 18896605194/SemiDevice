using System.Windows;
using System.Windows.Controls;
using xyz.Client.Manual.ViewModels;

namespace xyz.Client.Manual.Views;

/// <summary>
/// LoadPort 手动操作面板：花篮图 + 状态栏 + 五个动作按钮。
/// 通过 ModuleName 依赖属性实例化，每个 LoadPort 一个实例。
/// </summary>
public partial class LoadPortManualControl : UserControl
{
    public static readonly DependencyProperty ModuleNameProperty =
        DependencyProperty.Register(nameof(ModuleName), typeof(string), typeof(LoadPortManualControl),
            new PropertyMetadata(string.Empty, OnModuleNameChanged));

    public LoadPortManualControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 模块实例名（如 "LoadPort1"），与 EventBus token / gRPC 参数一致。
    /// </summary>
    public string ModuleName
    {
        get => (string)GetValue(ModuleNameProperty);
        set => SetValue(ModuleNameProperty, value);
    }

    private static void OnModuleNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not LoadPortManualControl control || e.NewValue is not string name || string.IsNullOrEmpty(name))
        {
            return;
        }

        // 只把 DataContext 挂在内容根节点上，不动控件自身的 DataContext：
        // 否则外层 <xxx ModuleName="{Binding}" /> 的绑定会因为 DataContext 变化二次求值，
        // 把 ViewModel 对象转成字符串再塞回 ModuleName。
        (control.Root.DataContext as LoadPortManualViewModel)?.Dispose();

        var viewModel = new LoadPortManualViewModel(name);
        control.Root.DataContext = viewModel;
        viewModel.Init();
    }
}
