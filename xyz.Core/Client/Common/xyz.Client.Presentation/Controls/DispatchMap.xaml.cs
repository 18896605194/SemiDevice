using System.Windows;
using System.Windows.Controls;
using xyz.Client.Presentation.ViewModels;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 调度图（公共控件，Robot 手动页和主界面都用）：给一个机械手模块名，控件自己建 ViewModel、订推送，
/// 机械手画在中间，站点卡片按它站点表里的方向摆在四周——sc.xml 里多一个腔体、一个 LoadPort，图上就多一张卡片，不用改界面。
/// 机型的主界面要别的摆法（两台机械手、缓存位……）时，自己写调度图，照样可以拿它、StationCard、Robot 来拼。
/// </summary>
public partial class DispatchMap : UserControl
{
    public static readonly DependencyProperty RobotNameProperty =
        DependencyProperty.Register(nameof(RobotName), typeof(string), typeof(DispatchMap),
            new PropertyMetadata(string.Empty, OnRobotNameChanged));

    public static readonly DependencyProperty CardWidthProperty =
        DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(DispatchMap),
            new PropertyMetadata(280.0));

    public static readonly DependencyProperty CardHeightProperty =
        DependencyProperty.Register(nameof(CardHeight), typeof(double), typeof(DispatchMap),
            new PropertyMetadata(233.0));

    public static readonly DependencyProperty RobotHeightProperty =
        DependencyProperty.Register(nameof(RobotHeight), typeof(double), typeof(DispatchMap),
            new PropertyMetadata(360.0, OnRobotHeightChanged));

    private static readonly DependencyPropertyKey DiskSizePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(DiskSize), typeof(double), typeof(DispatchMap),
            new PropertyMetadata(DiskSizeOf(360.0)));

    public static readonly DependencyProperty DiskSizeProperty = DiskSizePropertyKey.DependencyProperty;

    public DispatchMap()
    {
        InitializeComponent();

        // 先断开继承：还没给机械手名时内容根节点别拿外面的 DataContext 去求值（那是页面的 ViewModel，没有 Robot，只会报绑定错）。
        Root.DataContext = null;
    }

    /// <summary>
    /// 机械手模块名（如 Robot1），也是它状态推送的 token；换了就重建 ViewModel、重订推送。
    /// </summary>
    public string RobotName
    {
        get => (string)GetValue(RobotNameProperty);
        set => SetValue(RobotNameProperty, value);
    }

    /// <summary>
    /// 站点卡片宽（默认 280，近方形）。
    /// </summary>
    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    /// <summary>
    /// 站点卡片高（默认 233）。
    /// </summary>
    public double CardHeight
    {
        get => (double)GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    /// <summary>
    /// 机械手画多高（默认 360）；卡片上的圆片跟着它算，跟叉上的片一样大。
    /// </summary>
    public double RobotHeight
    {
        get => (double)GetValue(RobotHeightProperty);
        set => SetValue(RobotHeightProperty, value);
    }

    /// <summary>
    /// 卡片上圆片的直径 = 机械手叉上的片按 RobotHeight 缩放后的大小（只读，跟着 RobotHeight 变）。
    /// </summary>
    public double DiskSize => (double)GetValue(DiskSizeProperty);

    private static double DiskSizeOf(double robotHeight)
    {
        return robotHeight * Robot.WaferDiameter / Robot.DesignSize;
    }

    private static void OnRobotHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DispatchMap map)
        {
            map.SetValue(DiskSizePropertyKey, DiskSizeOf((double)e.NewValue));
        }
    }

    private static void OnRobotNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DispatchMap map)
        {
            return;
        }

        // 只把 DataContext 挂在内容根节点上，不动控件自身的 DataContext：
        // 外层 <DispatchMap RobotName="{Binding ...}" /> 的绑定要继续按外面的 DataContext 求值。
        (map.Root.DataContext as DispatchMapViewModel)?.Dispose();
        map.Root.DataContext = null;

        if (e.NewValue is not string name || string.IsNullOrEmpty(name))
        {
            return;
        }

        var viewModel = new DispatchMapViewModel(name);
        map.Root.DataContext = viewModel;
        viewModel.Init();
    }
}
