using System.Windows;

namespace ToolBuilder;

public partial class SourcesWindow : Window
{
    public SourcesWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
