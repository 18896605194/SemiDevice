using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using xyz.Client.Presentation.Controls;
using xyz.Tools;
using Microsoft.Win32;
using System.IO;
using xyz.Client.Presentation.Localization;

namespace CdsAssemblyPreview;

public partial class PreviewPage : UserControl
{
    private bool _embedded;
    public PreviewViewModel ViewModel => (PreviewViewModel)DataContext;
    public event Action? SaveRequested;
    public PreviewPage(bool embedded = false)
    {
        if (embedded) Resources = CdsEditorTheme.CreateEmbedded();
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<PreviewViewModel>();
        var model = (PreviewViewModel)DataContext;
        model.LayoutChanged += RefreshXamlPreview;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PreviewViewModel.Selected) && model.Selected is { } node)
                Dispatcher.BeginInvoke(() => ComponentsList.ScrollIntoView(node));
        };
        // 切换选中对象之前先提交正在输入的值，避免旧对象的草稿覆盖新对象。
        PreviewMouseDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is InputTextBox input && !input.IsMouseOver && !CommitEditor(input)) e.Handled = true;
        };
        PreviewKeyDown += OnPreviewKeyDown;
        ComponentsList.PreviewMouseLeftButtonUp += (_, _) => Scene.FocusEditor();
        PipePicker.DropDownClosed += (_, _) => Scene.FocusEditor();
    }
    public void ConfigureEmbedded()
    {
        _embedded = true; Resources = CdsEditorTheme.CreateEmbedded(); DesignRoot.Margin = new Thickness(0);
        foreach (var item in new FrameworkElement[] { EditorHeading, CanvasHeading, CanvasNote, ExampleButton, OpenLayoutButton, SaveLayoutButton, FileFooter, FileStatus, ComponentsTitle, ComponentsNote, ComponentsList }) item.Visibility = Visibility.Collapsed;
    }
    public string PrepareSave()
    {
        if (Keyboard.FocusedElement is InputTextBox input && IsAncestorOf(input) && !input.Commit()) throw new System.IO.InvalidDataException(xyz.Client.Presentation.Localization.L10n.Get("cdsp.parameters.invalid"));
        return ViewModel.PrepareSave();
    }
    public string PrepareXamlExport() => CdsDiagramXaml.Export(CdsLayout.Parse(PrepareSave()));
    public void ShowXaml(bool show)
    {
        PrepareSave(); Scene.PointerUp();
        XamlPreview.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Scene.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show) RefreshXamlPreview();
    }
    public void OpenXaml(string xaml)
    {
        PrepareSave();
        var layout = CdsDiagramXaml.ReadLayout(xaml);
        if (layout.ComponentRoot != ViewModel.Snapshot.ComponentRoot) throw new InvalidDataException(L10n.Get("cdsp.source.wrong_module"));
        Scene.PointerUp(); ViewModel.ApplyLayout(layout); RefreshXamlPreview();
    }
    public async Task ExportXamlAsync(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".xaml", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L10n.Get("cdsp.source.filter"));
        string xaml = PrepareXamlExport(), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, xaml); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void RefreshXamlPreview()
    {
        if (XamlPreview.Visibility != Visibility.Visible) return;
        try { XamlPreview.Text = CdsDiagramXaml.Export(ViewModel.Layout); }
        catch (Exception ex) { XamlPreview.Text = ex.Message; }
    }
    private void OnShowCanvas(object sender, RoutedEventArgs e) { try { ShowXaml(false); } catch (Exception ex) { ShowXamlError(ex); } }
    private void OnShowXaml(object sender, RoutedEventArgs e) { try { ShowXaml(true); } catch (Exception ex) { ShowXamlError(ex); } }
    private async void OnOpenXaml(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = L10n.Get("cdsp.source.filter") };
        if (dialog.ShowDialog() != true) return;
        try { OpenXaml(await File.ReadAllTextAsync(dialog.FileName)); } catch (Exception ex) { ShowXamlError(ex); }
    }
    private async void OnExportXaml(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = L10n.Get("cdsp.source.filter"), DefaultExt = ".xaml", AddExtension = true, FileName = ViewModel.Snapshot.ComponentRoot + ".xaml" };
        if (dialog.ShowDialog() != true) return;
        try { await ExportXamlAsync(dialog.FileName); } catch (Exception ex) { ShowXamlError(ex); }
    }
    private void ShowXamlError(Exception ex) => MessageBox.Show(Window.GetWindow(this), ex.Message, L10n.Get("cdsp.title"), MessageBoxButton.OK, MessageBoxImage.Error);
    public void PauseAnimation() => Visibility = Visibility.Hidden;
    internal static bool CommitEditor(InputTextBox input) => input.Commit() && (input.DataContext is not CdsTextParameter parameter || parameter.Error.Length == 0);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_embedded && e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { SaveRequested?.Invoke(); e.Handled = true; return; }
        if (TryDeleteSelection(e.Key, Keyboard.Modifiers, e.OriginalSource as DependencyObject)) { e.Handled = true; return; }
        if (TryMoveSelection(e.Key, Keyboard.Modifiers, e.OriginalSource as DependencyObject)) e.Handled = true;
    }
    internal bool TryDeleteSelection(Key key, ModifierKeys modifiers, DependencyObject? source)
    {
        if (key != Key.Delete || modifiers != ModifierKeys.None || IsEditingInput(source) || !ViewModel.DeleteSelectionCommand.CanExecute(null)) return false;
        Scene.PointerUp(); ViewModel.DeleteSelectionCommand.Execute(null); return true;
    }
    internal bool TryMoveSelection(Key key, ModifierKeys modifiers, DependencyObject? source)
    {
        if ((modifiers & ~ModifierKeys.Shift) != ModifierKeys.None || IsEditingInput(source)) return false;
        double step = modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        Vector delta = key switch { Key.Left => new(-step, 0), Key.Right => new(step, 0), Key.Up => new(0, -step), Key.Down => new(0, step), _ => default };
        if (delta == default || DataContext is not PreviewViewModel model || !model.NudgeCommand.CanExecute(delta)) return false;
        model.NudgeCommand.Execute(delta); return true;
    }
    internal static bool IsEditingInput(DependencyObject? source)
    {
        for (var current = source; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is TextBoxBase or PasswordBox or ComboBox or Slider or ScrollBar or TabItem) return true;
        return false;
    }
}
