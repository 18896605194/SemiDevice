using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using xyz.Client.Presentation.Localization;
using xyz.Tools;

namespace ChamberLayoutEditor;

public partial class ChamberEditorPage : UserControl
{
    public static BooleanToVisibilityConverter BooleanVisibility { get; } = new();
    private string? _path;
    private bool _embedded;
    public ChamberEditorViewModel ViewModel => (ChamberEditorViewModel)DataContext;
    public bool HasUnsavedChanges => ViewModel.IsDirty || ViewModel.IsDragging;
    public event Action? DocumentChanged;
    public event Action? SaveRequested;

    public ChamberEditorPage()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<ChamberEditorViewModel>();
        Scene.Bind(ViewModel);
        ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewModel.IsDirty)) DocumentChanged?.Invoke(); };
        ViewModel.PlacementChanged += () => DocumentChanged?.Invoke();
        PreviewKeyDown += OnKeyDown;
        Focusable = true;
    }

    public void ConfigureEmbedded() { _embedded = true; FileActions.Visibility = Visibility.Collapsed; }
    public void LoadDocument(ChamberLayoutDocument document) { Scene.CancelGesture(); ViewModel.Load(document); }
    public string PrepareSave() { Scene.FinishGesture(); return ViewModel.PrepareSave(); }
    public void AcceptSave(string path) { _path = path; ViewModel.MarkSaved(); FileStatus.Text = path; }

    public bool ConfirmClose()
    {
        Scene.CancelGesture();
        if (!HasUnsavedChanges) return true;
        return MessageBox.Show(Window.GetWindow(this), L10n.Get("chambereditor.discard"), L10n.Get("chambereditor.title"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = L10n.Get("chambereditor.layout_filter") };
        if (dialog.ShowDialog() != true || !ConfirmClose()) return;
        try
        {
            var document = ChamberLayoutDocument.Parse(await File.ReadAllTextAsync(dialog.FileName));
            LoadDocument(document); AcceptSave(dialog.FileName);
            ChamberPicker.ItemsSource = null; ChamberPicker.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_embedded) { SaveRequested?.Invoke(); return; }
        var dialog = new SaveFileDialog { Filter = L10n.Get("chambereditor.layout_filter"), FileName = _path ?? "ChamberLayout.xml", DefaultExt = ".xml", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        string temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string xml = PrepareSave();
            await File.WriteAllTextAsync(temporary, xml);
            File.Move(temporary, dialog.FileName, true);
            AcceptSave(dialog.FileName);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async void OnImportMachine(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "machine.xml|*.xml" };
        if (dialog.ShowDialog() != true || !ConfirmClose()) return;
        try
        {
            var documents = ChamberLayoutDocument.ReadMachine(await File.ReadAllTextAsync(dialog.FileName));
            ChamberPicker.ItemsSource = documents; ChamberPicker.Visibility = Visibility.Visible; ChamberPicker.SelectedIndex = 0;
            _path = null; FileStatus.Text = dialog.FileName;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OnChamberChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChamberPicker.SelectedItem is not ChamberLayoutDocument document) return;
        if (!ConfirmClose())
        {
            ChamberPicker.SelectionChanged -= OnChamberChanged;
            ChamberPicker.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            ChamberPicker.SelectionChanged += OnChamberChanged;
            return;
        }
        LoadDocument(ChamberLayoutDocument.Parse(document.ToXml()));
        _path = null;
    }
    private void OnDefaultView(object sender, RoutedEventArgs e) => Scene.DefaultView();
    private void ShowError(Exception ex) => MessageBox.Show(Window.GetWindow(this), ex.Message, L10n.Get("chambereditor.title"), MessageBoxButton.OK, MessageBoxImage.Error);
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Scene.CancelGesture(); e.Handled = true; }
        if (Keyboard.Modifiers != ModifierKeys.Control || Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.Z && ViewModel.UndoCommand.CanExecute(null)) { ViewModel.UndoCommand.Execute(null); e.Handled = true; }
        if (e.Key == Key.Y && ViewModel.RedoCommand.CanExecute(null)) { ViewModel.RedoCommand.Execute(null); e.Handled = true; }
        if (e.Key == Key.S) { OnSave(this, new RoutedEventArgs()); e.Handled = true; }
    }
}
