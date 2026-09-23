using System.Windows;
using System.Windows.Controls;
using xyz.Client.Manual.ViewModels;

namespace xyz.Client.Manual.Views;

/// <summary>
/// 机械手手动操作面板：转台图 + 状态横条 + 取放 / 运动 / 异常按钮与轴位表。
/// 通过 ModuleName 依赖属性实例化，每个 Robot 一个实例。
/// </summary>
public partial class RobotManualControl : UserControl
{
    public static readonly DependencyProperty ModuleNameProperty =
        DependencyProperty.Register(nameof(ModuleName), typeof(string), typeof(RobotManualControl),
            new PropertyMetadata(string.Empty, OnModuleNameChanged));

    public RobotManualControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 模块实例名（如 "Robot1"），与 EventBus token / gRPC 参数一致。
    /// </summary>
    public string ModuleName
    {
        get => (string)GetValue(ModuleNameProperty);
        set => SetValue(ModuleNameProperty, value);
    }

    private static void OnModuleNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RobotManualControl control || e.NewValue is not string name || string.IsNullOrEmpty(name))
        {
            return;
        }

        // 只把 DataContext 挂在内容根节点上，不动控件自身的 DataContext：
        // 否则外层 <xxx ModuleName="{Binding}" /> 的绑定会因为 DataContext 变化二次求值，
        // 把 ViewModel 对象转成字符串再塞回 ModuleName。
        (control.Root.DataContext as RobotManualViewModel)?.Dispose();

        var viewModel = new RobotManualViewModel(name);
        control.Root.DataContext = viewModel;
        viewModel.Init();
    }
}
