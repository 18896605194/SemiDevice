using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.DataCenter.Views;

/// <summary>
/// 曲线信息表：数据曲线、实时曲线两页共用，用所在页面的 DataContext（Series、CursorTime、VisibleStart、VisibleEnd）。
/// 值那一列的表头两页不一样（"光标处" / "当前值"），由页面给 ValueHeader。
/// </summary>
public partial class TrendInfoTable : UserControl
{
    public static readonly DependencyProperty ValueHeaderProperty = DependencyProperty.Register(
        nameof(ValueHeader), typeof(string), typeof(TrendInfoTable),
        new PropertyMetadata(string.Empty, (d, e) => ((TrendInfoTable)d).ValueColumn.Header = e.NewValue));

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
}
