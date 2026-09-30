using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.Views;

/// <summary>
/// 中间内容区：所有页面一次性挂进来，启动时一起套模板、排版，切页只切可见性。
/// 以前是一个内容槽、点到哪页才把哪页放进去——第一次点某页要现套模板、现排版，页面越复杂越卡，切走再切回还要重新挂、重新排。
/// 当前页 Visible，其余 Hidden：Hidden 仍参与排版（模板、布局都是现成的，切过去只剩绘制），但不画、点不到、拿不到焦点。
/// </summary>
public class PageHost : Grid
{
    public static readonly DependencyProperty PagesProperty = DependencyProperty.Register(
        nameof(Pages), typeof(IEnumerable), typeof(PageHost),
        new PropertyMetadata(null, (d, _) => ((PageHost)d).Rebuild()));

    public static readonly DependencyProperty CurrentProperty = DependencyProperty.Register(
        nameof(Current), typeof(object), typeof(PageHost),
        new PropertyMetadata(null, (d, _) => ((PageHost)d).ShowCurrent()));

    /// <summary>启动时建好的全部页面，一次性挂进来。</summary>
    public IEnumerable? Pages
    {
        get => (IEnumerable?)GetValue(PagesProperty);
        set => SetValue(PagesProperty, value);
    }

    /// <summary>当前要显示的页面；不在 Pages 里的（比如还没实现的占位页）用到时再挂。</summary>
    public object? Current
    {
        get => GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        foreach (var page in Pages ?? Array.Empty<object>())
        {
            if (page is UIElement element)
            {
                element.Visibility = Visibility.Hidden;
                Children.Add(element);
            }
        }

        ShowCurrent();
    }

    private void ShowCurrent()
    {
        if (Current is UIElement current && !Children.Contains(current))
        {
            Children.Add(current);
        }

        foreach (UIElement child in Children)
        {
            child.Visibility = ReferenceEquals(child, Current) ? Visibility.Visible : Visibility.Hidden;
        }
    }
}
