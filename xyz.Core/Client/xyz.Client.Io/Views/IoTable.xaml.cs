using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace xyz.Client.Io.Views;

/// <summary>
/// 一类 IO 点的表格。标题和数据源由外面传，DI/DO/AI/AO 四个区共用同一份布局与样式。
/// 输出表再传写命令（DO 给开/关，AO 给下发），表格才多出"操作"列；输入表不传，只读。
/// </summary>
public partial class IoTable : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(IoTable), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(ICollectionView), typeof(IoTable), new PropertyMetadata(null));

    public static readonly DependencyProperty TurnOnCommandProperty = RegisterCommand(nameof(TurnOnCommand));

    public static readonly DependencyProperty TurnOffCommandProperty = RegisterCommand(nameof(TurnOffCommand));

    public static readonly DependencyProperty SendCommandProperty = RegisterCommand(nameof(SendCommand));

    public IoTable()
    {
        InitializeComponent();
        UpdateActionColumn();
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

    /// <summary>DO 置 ON，参数是那一行的点；不给就没有开/关按钮。</summary>
    public ICommand? TurnOnCommand
    {
        get => (ICommand?)GetValue(TurnOnCommandProperty);
        set => SetValue(TurnOnCommandProperty, value);
    }

    /// <summary>DO 置 OFF，参数是那一行的点。</summary>
    public ICommand? TurnOffCommand
    {
        get => (ICommand?)GetValue(TurnOffCommandProperty);
        set => SetValue(TurnOffCommandProperty, value);
    }

    /// <summary>AO 下发，参数是那一行的点（设定值在点的 PendingValue）；不给就没有输入框和下发按钮。</summary>
    public ICommand? SendCommand
    {
        get => (ICommand?)GetValue(SendCommandProperty);
        set => SetValue(SendCommandProperty, value);
    }

    private static DependencyProperty RegisterCommand(string name)
    {
        return DependencyProperty.Register(name, typeof(ICommand), typeof(IoTable),
            new PropertyMetadata(null, (d, _) => ((IoTable)d).UpdateActionColumn()));
    }

    /// <summary>
    /// 列不在可视树里，Visibility 绑不上表格的属性，所以在这儿按有没有写命令切。
    /// </summary>
    private void UpdateActionColumn()
    {
        if (ActionColumn is null)
        {
            return;
        }

        bool writable = TurnOnCommand is not null || TurnOffCommand is not null || SendCommand is not null;
        ActionColumn.Visibility = writable ? Visibility.Visible : Visibility.Collapsed;
    }
}
