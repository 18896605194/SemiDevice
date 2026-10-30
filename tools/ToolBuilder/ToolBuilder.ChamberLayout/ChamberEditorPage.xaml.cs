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
    public bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;
    public event Action? DocumentChanged;
    public event Action? SaveRequested;

    public ChamberEditorPage()
    {
        Resources = ChamberTheme.Create();
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<ChamberEditorViewModel>();
        Scene.Bind(ViewModel);
        Scene.ViewPresetChanged += preset => ViewPicker.SelectedItem = ViewPicker.Items.Cast<ComboBoxItem>()
            .First(item => (string)item.Tag == preset.ToString());
        ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewModel.IsDirty)) DocumentChanged?.Invoke(); };
        ViewModel.PlacementChanged += () => DocumentChanged?.Invoke();
        PreviewKeyDown += OnKeyDown;
        Focusable = true;
    }

    public void ConfigureEmbedded()
    {
        _embedded = true; FileActions.Visibility = Visibility.Collapsed; EditorHeading.Visibility = Visibility.Collapsed;
        FileStatus.Visibility = Visibility.Collapsed;
        ComponentPanel.Visibility = Visibility.Collapsed; ComponentColumn.Width = new GridLength(0); ComponentGap.Width = new GridLength(0);
        EditorScaleHost.Child = null;
        DesignCanvas.Width = double.NaN; DesignCanvas.Height = double.NaN;
        Content = DesignCanvas;
    }
    public void LoadDocument(ChamberLayoutDocument document) { Scene.CancelGesture(); ViewModel.Load(document); }
    public void SetVisualCatalog(xyz.Client.Presentation.Controls.ThreeD.ChamberVisualCatalog catalog) => Scene.VisualCatalog = catalog;
    public void LoadMachine(string xml, string path = "", Func<string, string?>? resolveKind = null)
    {
        var configurations = ChamberLayoutDocument.ReadMachine(xml, resolveKind);
        Scene.CancelGesture(); ViewModel.LoadChambers(configurations);
        _path = null; FileStatus.Text = path;
    }
    public string PrepareSave() { Scene.FinishGesture(); return ViewModel.PrepareSave(); }
    public void AcceptSave(string path)
    {
        _path = path;
        if (_embedded) ViewModel.MarkSaved(); else ViewModel.MarkWorkspaceSaved();
        FileStatus.Text = path;
    }

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
            Scene.FinishGesture();
            ViewModel.ApplyLayouts(await File.ReadAllTextAsync(dialog.FileName));
            _path = dialog.FileName; FileStatus.Text = dialog.FileName;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasConfiguration) return;
        if (_embedded) { SaveRequested?.Invoke(); return; }
        var dialog = new SaveFileDialog { Filter = L10n.Get("chambereditor.layout_filter"), FileName = _path ?? "ChamberLayout.xml", DefaultExt = ".xml", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        string temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Scene.FinishGesture();
            string xml = ViewModel.PrepareWorkspaceSave();
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
            LoadMachine(await File.ReadAllTextAsync(dialog.FileName), dialog.FileName);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OnDefaultView(object sender, RoutedEventArgs e) => Scene.DefaultView();
    private void OnViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Scene is not null && ViewPicker.SelectedItem is ComboBoxItem item
            && Enum.TryParse<ChamberViewPreset>((string)item.Tag, out var preset)) Scene.SetViewPreset(preset);
    }
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
