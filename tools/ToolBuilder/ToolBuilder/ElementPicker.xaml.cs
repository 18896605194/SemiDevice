using System.Windows;
using ToolBuilder.Core;

namespace ToolBuilder;

public partial class ElementPicker : Window
{
    public ElementPickerViewModel ViewModel { get; }
    private readonly MainViewModel? _workspace;

    public ElementPicker(IEnumerable<ElementSpec> components, string parentPath, string category = "元件", MainViewModel? workspace = null)
    {
        _workspace = workspace;
        ViewModel = new(components, parentPath);
        ViewModel.Category = category;
        InitializeComponent();
        DataContext = ViewModel;
        SourcesButton.Visibility = workspace is null ? Visibility.Collapsed : Visibility.Visible;
        if (workspace is not null)
        {
            workspace.PropertyChanged += WorkspaceChanged;
            Closed += (_, _) => workspace.PropertyChanged -= WorkspaceChanged;
        }
        ViewModel.Confirmed += () => DialogResult = true;
        Loaded += (_, _) => { if (ViewModel.IsGroup) CategoryBox.Focus(); else SearchBox.Focus(); };
    }

    private void WorkspaceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsBusy) && _workspace?.IsBusy == false)
        {
            ViewModel.ReplaceElements(_workspace.Elements);
        }
    }

    private void ShowSourcesClick(object sender, RoutedEventArgs e)
    {
        if (_workspace is not null)
        {
            new SourcesWindow(_workspace) { Owner = this }.ShowDialog();
            ViewModel.ReplaceElements(_workspace.Elements);
        }
    }
}
