using System.Windows.Controls;
using System.Windows.Input;
using xyz.Client.Presentation.Controls;
using xyz.Tools;

namespace CdsAssemblyPreview;

public partial class ProcessPreviewPage : UserControl
{
    public ProcessPreviewPage()
    {
        InitializeComponent(); DataContext = IocHelper.GetRequiredService<ProcessPreviewViewModel>();
        PreviewMouseDown += (_, e) => { if (Keyboard.FocusedElement is InputTextBox box && !box.IsMouseOver && !box.Commit()) e.Handled = true; };
    }
    private void OnPumpReset(object sender, System.Windows.RoutedEventArgs e) => PumpScene.ResetCamera();
    private void OnPumpFront(object sender, System.Windows.RoutedEventArgs e) => PumpScene.FrontCamera();
    private void OnHeaterReset(object sender, System.Windows.RoutedEventArgs e) => HeaterScene.ResetCamera();
    private void OnHeaterFront(object sender, System.Windows.RoutedEventArgs e) => HeaterScene.FrontCamera();
}
