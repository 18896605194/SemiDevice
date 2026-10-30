using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using xyz.Client.Presentation.Controls;
using xyz.Tools;

namespace CdsAssemblyPreview;

public partial class HardwareCatalogPage : UserControl
{
    private Point? _dragOffset;
    public HardwareCatalogPage()
    {
        InitializeComponent(); DataContext = IocHelper.GetRequiredService<HardwareCatalogViewModel>();
        Cards.PreviewMouseLeftButtonUp += (_, _) => Keyboard.Focus(DemoStage);
        PreviewMouseDown += (_, e) => { if (Keyboard.FocusedElement is InputTextBox box && !box.IsMouseOver && !box.Commit()) e.Handled = true; };
        PreviewKeyDown += (_, e) =>
        {
            if (PreviewPage.IsEditingInput(e.OriginalSource as DependencyObject) || (Keyboard.Modifiers & ~ModifierKeys.Shift) != ModifierKeys.None) return;
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            Vector delta = e.Key switch { Key.Left => new(-step, 0), Key.Right => new(step, 0), Key.Up => new(0, -step), Key.Down => new(0, step), _ => default };
            if (delta == default) return;
            ((HardwareCatalogViewModel)DataContext).MoveSelected(delta); e.Handled = true;
        };
    }
    internal bool DemoPointerDown(Point point)
    {
        if (((HardwareCatalogViewModel)DataContext).Selected is not { } item) return false;
        var bounds = new RotateTransform(item.Angle, item.X + item.SymbolWidth / 2, item.Y + item.SymbolHeight / 2)
            .TransformBounds(new Rect(item.X, item.Y, item.SymbolWidth, item.SymbolHeight));
        if (!bounds.Contains(point)) return false;
        DemoStage.CaptureMouse(); _dragOffset = new Point(point.X - item.X, point.Y - item.Y); return true;
    }
    internal void DemoPointerMove(Point point)
    {
        if (_dragOffset is not { } offset || ((HardwareCatalogViewModel)DataContext).Selected is not { } item) return;
        item.X = point.X - offset.X; item.Y = point.Y - offset.Y;
    }
    internal void DemoPointerUp() { _dragOffset = null; if (DemoStage.IsMouseCaptured) DemoStage.ReleaseMouseCapture(); }
    private void OnDemoDown(object sender, MouseButtonEventArgs e) { Keyboard.Focus(DemoStage); e.Handled = DemoPointerDown(e.GetPosition(DemoStage)); }
    private void OnDemoMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DemoPointerMove(e.GetPosition(DemoStage)); }
    private void OnDemoUp(object sender, MouseButtonEventArgs e) => DemoPointerUp();
    private void OnDemoLostCapture(object sender, MouseEventArgs e) { if (!DemoStage.IsMouseCaptured) _dragOffset = null; }
}
