using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 按住类按钮（点动）：按下发一次 PressCommand；松开、鼠标拖出按钮、按钮被禁用或藏起来都发 ReleaseCommand，中间不发别的。
/// 两个命令共用 CommandParameter。不用 Button.Command（那是松开时才触发的单击）。松手一定要发出去，所以 Release 不看 CanExecute。
/// 样式照普通按钮写（Style 的 TargetType 写 Button 也能套）。
/// </summary>
public class HoldButton : Button
{
    public static readonly DependencyProperty PressCommandProperty = DependencyProperty.Register(
        nameof(PressCommand), typeof(ICommand), typeof(HoldButton), new PropertyMetadata(null));

    public static readonly DependencyProperty ReleaseCommandProperty = DependencyProperty.Register(
        nameof(ReleaseCommand), typeof(ICommand), typeof(HoldButton), new PropertyMetadata(null));

    /// <summary>正按着（发过 Press、还没发 Release）。</summary>
    private bool _holding;

    public HoldButton()
    {
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

    /// <summary>松手时发（如停止）。</summary>
    public ICommand? ReleaseCommand
    {
        get => (ICommand?)GetValue(ReleaseCommandProperty);
        set => SetValue(ReleaseCommandProperty, value);
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
    }

    private void End()
    {
        if (!_holding)
        {
            return;
        }

        _holding = false;
        ReleaseCommand?.Execute(CommandParameter);
    }
}
