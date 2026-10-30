using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using xyz.Client.Presentation.Controls;
using xyz.Tools;

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
