using System.Windows.Controls;

namespace CdsAssemblyPreview;

public partial class PreviewShell : UserControl
{
    public PreviewShell(PreviewPage editor, bool showLibrary, bool showProcess = false, bool showPipeline = false)
    {
        InitializeComponent(); EditorHost.Content = editor; Pages.SelectedIndex = showPipeline ? 3 : showProcess ? 2 : showLibrary ? 1 : 0;
    }
}
