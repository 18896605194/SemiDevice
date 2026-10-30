using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using xyz.Client.Presentation.Controls;
using xyz.Tools;

namespace CdsAssemblyPreview;

public partial class CdsPipelinePreviewPage : UserControl
{
    public CdsPipelinePreviewPage()
    {
        InitializeComponent(); DataContext = IocHelper.GetRequiredService<CdsPipelinePreviewViewModel>();
        PreviewMouseDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is InputTextBox input && !input.IsMouseOver && !input.Commit()) e.Handled = true;
        };
        PreviewKeyDown += (_, e) => { if (TryMoveSelection(e.Key, Keyboard.Modifiers, e.OriginalSource as DependencyObject)) e.Handled = true; };
        PartPicker.DropDownClosed += (_, _) => Scene.FocusEditor();
    }
    internal bool TryMoveSelection(Key key, ModifierKeys modifiers, DependencyObject? source)
    {
        if ((modifiers & ~ModifierKeys.Shift) != ModifierKeys.None || PreviewPage.IsEditingInput(source)) return false;
        double step = modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        Vector delta = key switch { Key.Left => new(-step, 0), Key.Right => new(step, 0), Key.Up => new(0, -step), Key.Down => new(0, step), _ => default };
        if (delta == default || DataContext is not CdsPipelinePreviewViewModel model) return false;
        model.NudgeCommand.Execute(delta); return true;
    }
}
