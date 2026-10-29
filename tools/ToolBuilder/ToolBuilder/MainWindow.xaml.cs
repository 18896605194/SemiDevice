using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using ToolBuilder.Core;

namespace ToolBuilder;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    /// <summary>"配方字段"页上按腔体 Type 生成的选项列。</summary>
    private readonly List<DataGridColumn> _optionColumns = [];

    public MainWindow(bool rememberSettings = true)
    {
        ViewModel = new(rememberSettings); InitializeComponent(); DataContext = ViewModel;
        ViewModel.NameEditRequested += () => Dispatcher.BeginInvoke(() =>
        {
            NodeNameEditor.Focus(); NodeNameEditor.SelectAll();
        });
        ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.RecipeOptionTypes)) BuildOptionColumns(); };
        RecipeFieldsGrid.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OptionButtonClick));
        BuildOptionColumns();
    }

    /// <summary>"配方字段"页的选项列：腔体配的 Type 一种一列（腔体都没配 Type 时只有一列"选项"），插在"必填"前面。</summary>
    private void BuildOptionColumns()
    {
        foreach (var column in _optionColumns) RecipeFieldsGrid.Columns.Remove(column);
        _optionColumns.Clear();
        var types = ViewModel.RecipeOptionTypes;
        for (var i = 0; i < types.Count; i++)
        {
            var header = types.Count == 1 && types[0].Length == 0 ? "选项（collection 才填）" : $"选项 · {(types[i].Length == 0 ? "没配 Type" : types[i])}";
            var cell = $"OptionCells[{i}]";
            var template = (DataTemplate)XamlReader.Parse(
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><DockPanel>"
                + $"<Button DockPanel.Dock=\"Right\" Content=\"…\" Width=\"30\" MinWidth=\"30\" Margin=\"4,0,0,0\" Padding=\"0\" IsEnabled=\"{{Binding HasOptions}}\" Tag=\"{{Binding {cell}}}\"/>"
                + $"<TextBox Text=\"{{Binding {cell}.Text, UpdateSourceTrigger=PropertyChanged}}\" IsEnabled=\"{{Binding HasOptions}}\" FontFamily=\"Consolas\" Style=\"{{DynamicResource GridEditorText}}\"/>"
                + "</DockPanel></DataTemplate>");
            var column = new DataGridTemplateColumn { Header = header, CellTemplate = template, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 180 };
            RecipeFieldsGrid.Columns.Insert(RecipeFieldsGrid.Columns.Count - 1, column);
            _optionColumns.Add(column);
        }
    }
    private void TreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is NodeModel node) ViewModel.Selected = node;
    }
    private double _treeWidth = 320;

    private void ToggleTreeClick(object sender, RoutedEventArgs e)
    {
        bool show = TreePanel.Visibility != Visibility.Visible;
        if (!show)
        {
            _treeWidth = TreeColumn.ActualWidth;
        }
        TreePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TreeColumn.MinWidth = show ? 220 : 0;
        TreeColumn.Width = new GridLength(show ? _treeWidth : 0);
        SplitterColumn.Width = new GridLength(show ? 12 : 0);
        TreeToggle.Content = show ? "收起元件树" : "展开元件树";
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!ViewModel.ConfirmDiscard()) e.Cancel = true;
    }
    private void IssueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: ValidationIssue issue }) ViewModel.NavigateIssue(issue);
    }
    private void ShowSourcesClick(object sender, RoutedEventArgs e)
    {
        var window = new SourcesWindow(ViewModel) { Owner = this };
        window.ShowDialog();
    }
    private void TreeItemRightClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source is not TreeViewItem)
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        if (source is TreeViewItem clicked && ReferenceEquals(sender, clicked)) { clicked.IsSelected = true; e.Handled = true; }
    }
    private void TreeItemSelected(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem item && ReferenceEquals(sender, e.OriginalSource))
            Dispatcher.BeginInvoke(() => item.BringIntoView());
    }

    /// <summary>"配方字段"页选项格旁边的"…"（按钮的 Tag 是那一格）：打开选项窗口。</summary>
    private void OptionButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button button && button.Tag is RecipeOptionCell cell) ViewModel.EditRecipeOptions(cell);
    }

    /// <summary>"配方头"页自己加的下拉旁边的"…"：打开选项窗口。</summary>
    private void EditHeadOptionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is RecipeFieldModel row && row.OptionCells.Count > 0)
            ViewModel.EditRecipeOptions(row.OptionCells[0]);
    }
}
