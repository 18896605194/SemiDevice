using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 调度图上的一个站点卡片（DataContext = RobotStationModel）：按站点类型选卡片——LoadPort 用 LoadPortInfoCard（花篮），
/// 腔体和其他站点用 ChamberInfoCard（单片）。卡片按真实字号排版、随给定宽高伸缩。
/// </summary>
public partial class StationCard : UserControl
{
    public static readonly DependencyProperty DiskSizeProperty =
        DependencyProperty.Register(nameof(DiskSize), typeof(double), typeof(StationCard),
            new PropertyMetadata(100.0));

    public StationCard()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 卡片上圆片的直径：跟调度图里机械手叉上的片一样大，由调度图按机械手的显示尺寸给。
    /// </summary>
    public double DiskSize
    {
        get => (double)GetValue(DiskSizeProperty);
        set => SetValue(DiskSizeProperty, value);
    }
}
