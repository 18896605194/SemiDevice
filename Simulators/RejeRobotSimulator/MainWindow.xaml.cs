using System.Windows;

namespace RejeRobotSimulator;

/// <summary>
/// 独立运行时的薄窗口: 只负责装下 <see cref="RobotPanel"/> 并在开关窗时驱动其生命周期。
/// 面板本体与 SimulatorHub 页签共用同一实现。
/// </summary>
public partial class MainWindow : Window
{
    private readonly RobotPanel _panel;

    public MainWindow(string dataDir)
    {
        InitializeComponent();
        ClampToWorkArea();

        _panel = new RobotPanel(dataDir);
        RootGrid.Children.Add(_panel);
        Title = "RejeRobot 机械手仿真器 — " + _panel.InstanceName;

        Loaded += (_, _) => _panel.StartListening();   // 打开即自动监听
        Closing += (_, _) => _panel.ShutdownForHost();
    }
    /// <summary>
    /// 目标 1280x720，但不超过主屏工作区 90%——高 DPI 小屏上不再铺满整屏。
    /// </summary>
    private void ClampToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        if (Width > work.Width * 0.9)
        {
            Width = work.Width * 0.9;
        }

        if (Height > work.Height * 0.9)
        {
            Height = work.Height * 0.9;
        }
    }
}
