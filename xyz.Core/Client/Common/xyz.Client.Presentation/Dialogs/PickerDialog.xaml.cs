using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 公共选择弹窗：从一个库里选一项。标题、列、数据由用的地方给，单选；点一行选中，双击或"确定"带回，Esc 或"取消"关掉。
/// 一般经 DialogService.ShowPicker 弹出（选择框 PickerBox 点"…"就是这么弹的）。
/// </summary>
public partial class PickerDialog : Window
{
    public PickerDialog(string title, IEnumerable<PickerColumn> columns, IEnumerable items, string? currentValue, string? valuePath)
    {
        InitializeComponent();

        TitleText.Text = title;
        foreach (var column in columns)
        {
            ItemsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = L10n.Get(column.HeaderKey),
                Binding = new Binding(column.Path) { Mode = BindingMode.OneWay },
                Width = double.IsNaN(column.Width)
                    ? new DataGridLength(1, DataGridLengthUnitType.Star)
                    : new DataGridLength(column.Width),
                ElementStyle = (Style)FindResource("EllipsisDataGridTextStyle"),
            });
        }

        var rows = items.Cast<object>().ToList();
        ItemsGrid.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 先选中跟现在的值对得上的那一项（如选择框里现在填的），打开就看得到
        if (!string.IsNullOrEmpty(currentValue) && !string.IsNullOrEmpty(valuePath))
        {
            var current = rows.FirstOrDefault(row => string.Equals(ValueOf(row, valuePath), currentValue, StringComparison.OrdinalIgnoreCase));
            if (current is not null)
            {
                ItemsGrid.SelectedItem = current;
                Loaded += (_, _) => ItemsGrid.ScrollIntoView(current);
            }
        }
    }

    /// <summary>
    /// 选中的那一项；没选为 null。
    /// </summary>
    public object? SelectedItem => ItemsGrid.SelectedItem;

    /// <summary>
    /// 数据项某个属性的值（转成字符串）；没有这个属性就是空字符串。
    /// </summary>
    public static string ValueOf(object item, string path)
    {
        return item.GetType().GetProperty(path)?.GetValue(item)?.ToString() ?? string.Empty;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        OkButton.IsEnabled = ItemsGrid.SelectedItem is not null;
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs args)
    {
        // 双击在表头、空白处不算
        if (args.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ItemsGrid, source) is DataGridRow
            && ItemsGrid.SelectedItem is not null)
        {
            DialogResult = true;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs args)
    {
        if (ItemsGrid.SelectedItem is not null)
        {
            DialogResult = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs args)
    {
        DialogResult = false;
    }
}
