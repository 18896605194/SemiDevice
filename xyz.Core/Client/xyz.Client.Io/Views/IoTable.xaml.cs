using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.Io.Views;

/// <summary>
/// 一类 IO 点的表格。标题和数据源由外面传，DI/DO/AI/AO 四个区共用同一份布局与样式。
/// </summary>
public partial class IoTable : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(IoTable), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(ICollectionView), typeof(IoTable), new PropertyMetadata(null));

    public IoTable()
    {
        InitializeComponent();
    }

    /// <summary>区标题（走语言包，如 io.di）。</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>这一类点的视图（带搜索过滤）。</summary>
    public ICollectionView? Points
    {
        get => (ICollectionView?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }
}
