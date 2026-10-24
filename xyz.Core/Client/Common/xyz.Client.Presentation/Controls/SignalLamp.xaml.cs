using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 信号灯：一盏灯 + 名字，对一个 DI 信号
/// </summary>
public partial class SignalLamp : UserControl
{
    public SignalLamp()
    {
        InitializeComponent();
        Apply();
    }

    /// <summary>
    /// 名字（machine.xml 里的节点名，原样显示）
    /// </summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SignalLamp), new PropertyMetadata(string.Empty, OnChanged));

    /// <summary>
    /// 有没有触发，读不到是 null
    /// </summary>
    public bool? IsActive
    {
        get => (bool?)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool?), typeof(SignalLamp), new PropertyMetadata(null, OnChanged));

    /// <summary>
    /// 报警位（HH、Overfill 这种）：触发了亮红
    /// </summary>
    public bool IsAlarm
    {
        get => (bool)GetValue(IsAlarmProperty);
        set => SetValue(IsAlarmProperty, value);
    }

    public static readonly DependencyProperty IsAlarmProperty = DependencyProperty.Register(
        nameof(IsAlarm), typeof(bool), typeof(SignalLamp), new PropertyMetadata(false, OnChanged));

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((SignalLamp)sender).Apply();
    }

    /// <summary>
    /// 触发亮绿（报警位亮红）、没触发灭、读不到是"不知道"色
    /// </summary>
    private void Apply()
    {
        NameText.Text = Text;
        bool? active = IsActive;
        if (active is null)
        {
            Lamp.Tag = "Unknown";
            return;
        }

        if (active == false)
        {
            Lamp.Tag = "Off";
            return;
        }

        if (IsAlarm)
        {
            Lamp.Tag = "Alarm";
            return;
        }

        Lamp.Tag = "On";
    }
}
