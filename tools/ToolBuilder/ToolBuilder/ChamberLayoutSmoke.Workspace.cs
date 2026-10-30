using ChamberLayoutEditor;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace ToolBuilder;

internal static partial class ChamberLayoutSmoke
{
    private static async Task WorkspaceChecks(MainWindow window, ChamberEditorPage editor, ChamberLayoutScene scene,
        FrameworkElement content, string output, Action<bool, string> check)
    {
        var model = editor.ViewModel; int originalTab = model.SelectedInspectorTab;
        string original = model.PrepareSave(); bool dirty = model.IsDirty;
        async Task Pump() { await Task.Delay(80); await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        async Task Click(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Pump(); }
        model.SelectedInspectorTab = 0; await Pump();
        var sidebar = (Border)editor.FindName("InspectorPanel");
        check(scene.ActualHeight >= editor.ActualHeight - 4 && scene.ActualWidth >= 850 && sidebar.ActualWidth == 280,
            "腔体画布和CDS一致占满左侧全高，右侧属性栏宽280");
        check(new[] { "ViewActions", "ZoomPanel", "EditingActions" }.All(name =>
            ((FrameworkElement)editor.FindName(name)).TransformToAncestor(editor).Transform(new Point()).X > scene.ActualWidth)
            && window.SelectionHeading.Visibility == Visibility.Collapsed,
            "视角、缩放、撤销重做均放右侧，不占画布上下区域或重复显示大标题");
        check(scene.AspectCompensation == (1d, 1d), "自适应腔体画面不拉伸原生三维模型");
        double width = scene.CameraWidth;
        await Click((Button)editor.FindName("ZoomInButton"));
        check(scene.ZoomPercent == 120 && scene.CameraWidth < width, "腔体右侧放大按钮实际放大三维画面");
        await Click((Button)editor.FindName("ZoomOutButton"));
        check(scene.ZoomPercent == 100 && Math.Abs(scene.CameraWidth - width) < 1e-6, "缩小按钮按同一比例还原");
        var input = (xyz.Client.Presentation.Controls.InputTextBox)editor.FindName("ZoomInput"); input.Text = "150";
        check(input.Commit(), "腔体缩放使用已有合法数值输入控件"); await Pump();
        check(scene.ZoomPercent == 150 && Math.Abs(scene.CameraWidth * 1.5 - width) < 1e-6, "输入150%后相机按比例缩放");
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        scene.RaiseEvent(wheel); await Pump();
        check(wheel.Handled && scene.ZoomPercent == 180 && input.Value == "180", "滚轮缩放同步右侧比例显示");
        var origin = scene.ProbeOrigin(model.Selected!.Id); var projected = scene.Project(origin);
        check((scene.Unproject(projected, origin) - origin).Length < 1e-6, "缩放后投影和拖动坐标换算保持准确");
        var picker = (ComboBox)editor.FindName("ViewPicker"); picker.SelectedIndex = 1; await Pump();
        check(scene.ViewPreset == ChamberViewPreset.Top && scene.ZoomPercent == 180, "侧栏切换俯视保留手动缩放比例");
        await Click((Button)editor.FindName("FitButton"));
        var bounds = scene.ContentBounds;
        var corners = (from x in new[] { bounds.X, bounds.X + bounds.SizeX }
                       from y in new[] { bounds.Y, bounds.Y + bounds.SizeY }
                       from z in new[] { bounds.Z, bounds.Z + bounds.SizeZ }
                       select scene.Project(new Point3D(x, y, z))).ToArray();
        check(scene.ViewPreset == ChamberViewPreset.Top && corners.All(p => p.X >= 0 && p.Y >= 0 && p.X <= scene.ActualWidth && p.Y <= scene.ActualHeight),
            "适应窗口在保留当前视角的同时完整容纳三维组件");
        await Click((Button)editor.FindName("DefaultViewButton"));
        check(scene.ViewPreset == ChamberViewPreset.Perspective && scene.ZoomPercent == 100 && picker.SelectedIndex == 0,
            "默认视角恢复三维方向和100%且下拉框同步");
        scene.ZoomPercent = 999; check(scene.ZoomPercent == 400 && !((Button)editor.FindName("ZoomInButton")).IsEnabled, "腔体缩放上限400%");
        scene.ZoomPercent = 1; check(scene.ZoomPercent == 10 && !((Button)editor.FindName("ZoomOutButton")).IsEnabled, "腔体缩放下限10%");
        await Click((Button)editor.FindName("ActualSizeButton"));
        check(scene.ZoomPercent == 100 && model.PrepareSave() == original && model.IsDirty == dirty, "视角及缩放不修改布局坐标、尺寸和未保存状态");
        Capture(content, Path.Combine(output, "chamber-workspace.png"));
        window.TreeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        content.Measure(new Size(1200, 800)); content.Arrange(new Rect(0, 0, 1200, 800)); content.UpdateLayout(); await Pump();
        check(scene.ActualWidth >= 850 && scene.ActualHeight >= 600 && scene.ActualHeight >= editor.ActualHeight - 4,
            "1200x800窗口收起元件树后，腔体画布仍保持大于850x600的显示区域");
        File.WriteAllText(Path.Combine(output, "chamber-canvas-size.txt"), $"Window 1200x800; chamber viewport {scene.ActualWidth:0}x{scene.ActualHeight:0}; sidebar {sidebar.ActualWidth:0}");
        Capture(content, Path.Combine(output, "chamber-workspace-small.png"));
        window.TreeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        content.Measure(new Size(1600, 1000)); content.Arrange(new Rect(0, 0, 1600, 1000)); content.UpdateLayout(); await Pump();
        model.SelectedInspectorTab = originalTab;
    }
}
