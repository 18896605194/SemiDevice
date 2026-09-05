using System.Windows;

namespace FcdLoadPortSimulator;

/// <summary>
/// 独立运行时的薄窗口: 只负责装下 <see cref="LoadPortPanel"/> 并在开关窗时驱动其生命周期。
/// 面板本体与 SimulatorHub 页签共用同一实现。
/// </summary>
public partial class MainWindow : Window
{
    private readonly LoadPortPanel _panel;

    public MainWindow(string dataDir)
    {
        InitializeComponent();
        ClampToWorkArea();

        _panel = new LoadPortPanel(dataDir);
        RootGrid.Children.Add(_panel);
        Title = "FcdLoadPort (富创得 LP300) 仿真器 — " + _panel.InstanceName;

        Loaded += (_, _) => _panel.OpenSelectedPort(silent: true);   // 打开即自动开默认串口
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
