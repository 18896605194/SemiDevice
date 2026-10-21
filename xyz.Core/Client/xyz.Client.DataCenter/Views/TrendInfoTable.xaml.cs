using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 曲线信息表：数据曲线、实时曲线两页共用，用所在页面的 DataContext（Series、CursorTime、VisibleStart、VisibleEnd）。
/// 值那一列的表头两页不一样（"光标处" / "当前值"），由页面给 ValueHeader；"X1 处 / X2 处"两列跟着图上的 X 基准线开关。
/// </summary>
public partial class TrendInfoTable : UserControl
{
    public static readonly DependencyProperty ValueHeaderProperty = DependencyProperty.Register(
        nameof(ValueHeader), typeof(string), typeof(TrendInfoTable),
        new PropertyMetadata(string.Empty, (d, e) => ((TrendInfoTable)d).ValueColumn.Header = e.NewValue));

    public static readonly DependencyProperty ShowReferenceColumnsProperty = DependencyProperty.Register(
        nameof(ShowReferenceColumns), typeof(bool), typeof(TrendInfoTable),
        new PropertyMetadata(false, (d, e) => ((TrendInfoTable)d).OnShowReferenceColumnsChanged((bool)e.NewValue)));

    public TrendInfoTable()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 值那一列的表头（DataGrid 的列不在可视树里，绑不上页面资源，从这里转一手）。
    /// </summary>
    public string ValueHeader
    {
        get => (string)GetValue(ValueHeaderProperty);
        set => SetValue(ValueHeaderProperty, value);
    }

    /// <summary>
    /// 显示"X1 处 / X2 处"两列（图上开着 X 基准线时）。
    /// </summary>
    public bool ShowReferenceColumns
    {
        get => (bool)GetValue(ShowReferenceColumnsProperty);
        set => SetValue(ShowReferenceColumnsProperty, value);
    }

    private void OnShowReferenceColumnsChanged(bool show)
    {
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AtX1Column.Visibility = visibility;
        AtX2Column.Visibility = visibility;
    }
}
