using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace xyz.Client.Presentation.Helpers;

/// <summary>
/// 有星号列的表格：各列宽度始终铺满可视宽度。公共表格样式（DefaultDataGridStyle 及派生的 Compact / Dense）里统一挂上，页面不用管。
/// 为什么要它：WPF 的 DataGrid 一加列就排一次"算列宽"，要是轮到它时表格还没进窗口（没有可视宽度，按 0 算），
/// 固定宽的列全被压到最小列宽 20，星号列也是 20。本来等可视宽度出来，DataGrid 会把压掉的宽度还回去，
/// 可"还"的那一步读的是内部滚动区的可视宽度，那个值要等整轮排版做完才刷新——启动时全部页面一起排版要好几秒，
/// 常常赶不上，读到 0 就什么也没还；之后页面大小固定、可视宽度不再变，这些列就一直是 20：
/// 单元格左右的内边距比列还宽，表头和内容的字全被裁掉，看着像一张空表（报警两页就是这样）。
/// 客户端启动时先建好全部页面、过一会儿才挂进主窗口（见 PageHost），哪张表中招全看时序，所以放在公共样式里兜底。
/// </summary>
public static class DataGridColumnFill
{
    /// <summary>
    /// 判"没铺满"的容差：各列宽度之和比可用宽度少出这么多才算（排版取整会差零点几）。
    /// </summary>
    private const double FillTolerance = 1.0;

    /// <summary>
    /// 是否启用。
    /// </summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DataGridColumnFill),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>
    /// 取是否启用。
    /// </summary>
    public static bool GetIsEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsEnabledProperty);
    }

    /// <summary>
    /// 设是否启用。
    /// </summary>
    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DataGrid grid)
        {
            return;
        }

        grid.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        if ((bool)e.NewValue)
        {
            grid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        }
    }

    /// <summary>
    /// 表格自己的滚动区可视宽度变了（这时它的 ViewportWidth 已经是新值）。
    /// DataGrid 自己"还宽度"排在 Loaded 优先级，这里排到更低的 Background 再看：正常情况那时它已经补好了，
    /// 只有它读错宽度没补上的才动手；竖向滚动条出现、消失这类正常变化不会误判。
    /// </summary>
    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 单元格里带滚动的控件，它们的 ScrollChanged 也会冒到表格上来，只认表格模板里自己的那个
        if (e.ViewportWidthChange == 0
            || sender is not DataGrid grid
            || e.OriginalSource is not ScrollViewer viewer
            || !ReferenceEquals(viewer.TemplatedParent, grid))
        {
            return;
        }

        grid.Dispatcher.InvokeAsync(() => RefillIfNeeded(grid, viewer), DispatcherPriority.Background);
    }

    /// <summary>
    /// 有星号列却没铺满可视宽度：把最后一列拿下来再原样放回去。
    /// DataGrid 的列一增减就会按当前可视宽度把所有列重新分一遍（启动时 XAML 加列走的就是这条路），
    /// 固定宽的列回到设计宽度，星号列分剩下的；放回后显示顺序照旧。
    /// </summary>
    private static void RefillIfNeeded(DataGrid grid, ScrollViewer viewer)
    {
        var hasStar = false;
        var used = 0.0;
        foreach (var column in grid.Columns)
        {
            if (column.Visibility != Visibility.Visible)
            {
                continue;
            }

            used += column.ActualWidth;
            hasStar = hasStar || column.Width.IsStar;
        }

        // 显示行头时，行头那一截不归各列
        var rowHeaderWidth = grid.HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row) ? grid.RowHeaderActualWidth : 0;
        var available = viewer.ViewportWidth - rowHeaderWidth;
        if (!hasStar || used >= available - FillTolerance)
        {
            return;
        }

        var index = grid.Columns.Count - 1;
        var last = grid.Columns[index];
        var displayIndex = last.DisplayIndex;
        grid.Columns.RemoveAt(index);
        grid.Columns.Insert(index, last);
        last.DisplayIndex = displayIndex;
    }
}
