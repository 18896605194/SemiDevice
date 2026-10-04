using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 按住类按钮（点动）：按下发 PressCommand；按住期间每隔 RenewMilliseconds 发一次 RenewCommand（后台据此知道界面还按着，
/// 续不上会自己停）；松开、鼠标拖出按钮、按钮被禁用或藏起来都发 ReleaseCommand。三个命令共用 CommandParameter。
/// 不用 Button.Command（那是松开时才触发的单击）。松手一定要发出去，所以 Release、Renew 不看 CanExecute。
/// 样式照普通按钮写（Style 的 TargetType 写 Button 也能套）。
/// </summary>
public class HoldButton : Button
{
    /// <summary>按住期间多久续一次（毫秒）；后台的保活超时（腔体 EC HoldTimeoutMs，默认 1000）要比它大几倍。</summary>
    public const int DefaultRenewMilliseconds = 200;

    public static readonly DependencyProperty PressCommandProperty = DependencyProperty.Register(
        nameof(PressCommand), typeof(ICommand), typeof(HoldButton), new PropertyMetadata(null));

    public static readonly DependencyProperty RenewCommandProperty = DependencyProperty.Register(
        nameof(RenewCommand), typeof(ICommand), typeof(HoldButton), new PropertyMetadata(null));

    public static readonly DependencyProperty ReleaseCommandProperty = DependencyProperty.Register(
        nameof(ReleaseCommand), typeof(ICommand), typeof(HoldButton), new PropertyMetadata(null));

    public static readonly DependencyProperty RenewMillisecondsProperty = DependencyProperty.Register(
        nameof(RenewMilliseconds), typeof(int), typeof(HoldButton),
        new PropertyMetadata(DefaultRenewMilliseconds, OnRenewMillisecondsChanged), value => value is int milliseconds && milliseconds > 0);

    private readonly DispatcherTimer _timer;

    /// <summary>正按着（发过 Press、还没发 Release）。</summary>
    private bool _holding;

    public HoldButton()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DefaultRenewMilliseconds) };
        _timer.Tick += (_, _) => Run(RenewCommand);
        IsVisibleChanged += (_, args) =>
        {
            if (!(bool)args.NewValue)
            {
                End();
            }
        };
        IsEnabledChanged += (_, args) =>
        {
            if (!(bool)args.NewValue)
            {
                End();
            }
        };
        Unloaded += (_, _) => End();
    }

    /// <summary>按下时发（如点动开始）。</summary>
    public ICommand? PressCommand
    {
        get => (ICommand?)GetValue(PressCommandProperty);
        set => SetValue(PressCommandProperty, value);
    }

    /// <summary>按住期间隔一会儿发一次（如续点动）。</summary>
    public ICommand? RenewCommand
    {
        get => (ICommand?)GetValue(RenewCommandProperty);
        set => SetValue(RenewCommandProperty, value);
    }

    /// <summary>松手时发（如停止）。</summary>
    public ICommand? ReleaseCommand
    {
        get => (ICommand?)GetValue(ReleaseCommandProperty);
        set => SetValue(ReleaseCommandProperty, value);
    }

    /// <summary>按住期间多久续一次（毫秒），默认 <see cref="DefaultRenewMilliseconds"/>。</summary>
    public int RenewMilliseconds
    {
        get => (int)GetValue(RenewMillisecondsProperty);
        set => SetValue(RenewMillisecondsProperty, value);
    }

    private static void OnRenewMillisecondsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((HoldButton)sender)._timer.Interval = TimeSpan.FromMilliseconds((int)args.NewValue);
    }

    /// <summary>按下 / 松开都看 IsPressed：鼠标按住拖出按钮也算松手（停下来），拖回来算重新按下。</summary>
    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        if ((bool)e.NewValue)
        {
            Begin();
        }
        else
        {
            End();
        }
    }

    private void Begin()
    {
        if (_holding)
        {
            return;
        }

        var press = PressCommand;
        object? parameter = CommandParameter;
        if (press is null || !press.CanExecute(parameter))
        {
            return;
        }

        _holding = true;
        press.Execute(parameter);
        _timer.Start();
    }

    private void End()
    {
        if (!_holding)
        {
            return;
        }

        _holding = false;
        _timer.Stop();
        Run(ReleaseCommand);
    }

    private void Run(ICommand? command)
    {
        command?.Execute(CommandParameter);
    }
}
