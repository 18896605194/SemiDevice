using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.Views;

/// <summary>
/// 中间内容区域的占位页，后续实际模块页面做好后替换即可。
/// </summary>
public partial class PlaceholderView : UserControl
{
    public PlaceholderView()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(PlaceholderView),
            new PropertyMetadata(string.Empty));

}
