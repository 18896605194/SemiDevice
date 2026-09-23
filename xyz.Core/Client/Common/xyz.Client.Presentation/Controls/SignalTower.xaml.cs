using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 四色灯（竖排）：红 = 报警、黄 = 警告、绿 = 运行、蓝 = 通讯。只管显示，亮不亮由外面绑定四个开关。
/// </summary>
public partial class SignalTower : UserControl
{
    /// <summary>
    /// 灭灯时灯芯的透明度：只剩一点底色，配上本色描边，一眼分得出亮灭。
    /// </summary>
    private const double DimOpacity = 0.14;

    /// <summary>
    /// 亮灯时光晕的透明度。
    /// </summary>
    private const double GlowOpacity = 0.9;

    public static readonly DependencyProperty IsRedOnProperty = RegisterLamp(nameof(IsRedOn));

    public static readonly DependencyProperty IsYellowOnProperty = RegisterLamp(nameof(IsYellowOn));

    public static readonly DependencyProperty IsGreenOnProperty = RegisterLamp(nameof(IsGreenOn));

    public static readonly DependencyProperty IsBlueOnProperty = RegisterLamp(nameof(IsBlueOn));

    public SignalTower()
    {
        InitializeComponent();
        UpdateLamps();
    }

    /// <summary>红灯：报警。</summary>
    public bool IsRedOn
    {
        get => (bool)GetValue(IsRedOnProperty);
        set => SetValue(IsRedOnProperty, value);
    }

    /// <summary>黄灯：警告。</summary>
    public bool IsYellowOn
    {
        get => (bool)GetValue(IsYellowOnProperty);
        set => SetValue(IsYellowOnProperty, value);
    }

    /// <summary>绿灯：运行。</summary>
    public bool IsGreenOn
    {
        get => (bool)GetValue(IsGreenOnProperty);
        set => SetValue(IsGreenOnProperty, value);
    }

    /// <summary>蓝灯：通讯。</summary>
    public bool IsBlueOn
    {
        get => (bool)GetValue(IsBlueOnProperty);
        set => SetValue(IsBlueOnProperty, value);
    }

    private static DependencyProperty RegisterLamp(string name)
    {
        return DependencyProperty.Register(name, typeof(bool), typeof(SignalTower),
            new PropertyMetadata(false, (d, _) => ((SignalTower)d).UpdateLamps()));
    }

    private void UpdateLamps()
    {
        SetLamp(RedLamp, RedGlow, IsRedOn);
        SetLamp(YellowLamp, YellowGlow, IsYellowOn);
        SetLamp(GreenLamp, GreenGlow, IsGreenOn);
        SetLamp(BlueLamp, BlueGlow, IsBlueOn);
    }

    private static void SetLamp(UIElement lamp, DropShadowEffect glow, bool isOn)
    {
        lamp.Opacity = isOn ? 1 : DimOpacity;
        glow.Opacity = isOn ? GlowOpacity : 0;
    }
}
