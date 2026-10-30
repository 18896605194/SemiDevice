using CdsAssemblyPreview;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ToolBuilder;

internal static partial class ToolBuilderSmoke
{
    private static async Task ZoomChecks(PreviewPage editor, Action<bool, string> check, Func<string, Task> screenshot)
    {
        var scene = editor.Scene; var model = editor.ViewModel; string original = model.PrepareSave();
        async Task Pump() { await Task.Delay(80); await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        async Task Click(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Pump(); }
        check(scene.IsAutoFit && scene.ZoomPercent > 10 && scene.ZoomPercent < 400
            && scene.Scroller.ScrollableWidth == 0 && scene.Scroller.ScrollableHeight == 0, "默认整体适应窗口，完整显示画布");
        double fit = scene.ZoomPercent;
        await Click(editor.ZoomInButton);
        check(!scene.IsAutoFit && scene.ZoomPercent > fit, "放大按钮切换为手动整体缩放");
        await Click(editor.ZoomOutButton);
        check(Math.Abs(scene.ZoomPercent - fit) <= 0.1, "缩小按钮按比例缩小整个画布");
        editor.ZoomInput.Text = "150"; check(editor.ZoomInput.Commit(), "缩放比例使用现有数值输入控件提交"); await Pump();
        check(scene.ZoomPercent == 150 && scene.SceneScale.ScaleX == 1.5 && scene.SceneScale.ScaleY == 1.5,
            "输入150%后二维叠加层和三维槽/管路同步等比缩放");
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        scene.Scroller.RaiseEvent(wheel); await Pump();
        check(wheel.Handled && scene.ZoomPercent == 180, "画布滚轮事件直接放大视图");
        scene.ZoomPercent = 200; await Pump();
        scene.Scroller.ScrollToHorizontalOffset(200); scene.Scroller.ScrollToVerticalOffset(140); await Pump();
        var anchor = new Point(scene.Scroller.ViewportWidth / 2, scene.Scroller.ViewportHeight / 2);
        var point = scene.Scroller.TranslatePoint(anchor, scene.Stage); scene.ZoomAt(120, anchor); await Pump();
        check((scene.Stage.TranslatePoint(point, scene.Scroller) - anchor).Length < 1,
            "放大时保持鼠标所指画布位置，避免画面跳动");
        check(scene.Scroller.HorizontalOffset > 0 && scene.Scroller.VerticalOffset > 0, "放大后可用水平和垂直滚动条移动视图");
        check(model.PrepareSave() == original, "缩放和移动视图不修改组件位置、尺寸或布局文件内容");
        var tank = model.Layout.Nodes.First(n => n.Kind == "Tank");
        var start = new Point(tank.X + tank.Width / 2, tank.Y + tank.Height / 2);
        var screen = scene.Stage.TranslatePoint(start, scene.Scroller);
        check(scene.PointerDown(scene.Scroller.TranslatePoint(screen, scene.Stage)) && model.Selected?.Id == tank.Id,
            "放大且滚动后命中测试仍选中正确组件");
        scene.PointerMove(start + new Vector(29, 17)); scene.PointerUp();
        check(model.Layout.Nodes.Single(n => n.Id == tank.Id).X > tank.X && model.Layout.Nodes.Single(n => n.Id == tank.Id).Y > tank.Y,
            "放大且滚动后拖动仍按布局坐标移动组件，连接管路跟随");
        double x = model.PositionX;
        check(editor.TryMoveSelection(Key.Right, ModifierKeys.None, scene.Stage) && model.PositionX == x + 1,
            "缩放后方向键仍按一个布局单位微调");
        model.ApplyLayout(CdsLayout.Parse(original));
        model.ConnectMode = true;
        var valve = model.Layout.Nodes.First(n => n.Kind == "Valve");
        foreach (string port in new[] { tank.Id + ".Inlet", valve.Id + ".Outlet" })
        {
            var p = model.Layout.Port(port); var local = new Point(p.X, -p.Y);
            var viewed = scene.Stage.TranslatePoint(local, scene.Scroller);
            scene.PointerDown(scene.Scroller.TranslatePoint(viewed, scene.Stage));
        }
        check(model.PipeCount == 2, "缩放后连接端口仍命中正确位置并创建管路");
        model.ConnectMode = false; model.ApplyLayout(CdsLayout.Parse(original));
        await screenshot("toolbuilder-cds-zoomed.png");
        await Click(editor.ActualSizeButton);
        check(scene.ZoomPercent == 100 && !scene.IsAutoFit, "100%按钮恢复实际显示比例");
        scene.ZoomPercent = 999; check(scene.ZoomPercent == 400 && !editor.ZoomInButton.IsEnabled, "整体放大上限400%且按钮状态同步");
        scene.ZoomPercent = 1; check(scene.ZoomPercent == 10 && !editor.ZoomOutButton.IsEnabled, "整体缩小下限10%且按钮状态同步");
        await Click(editor.FitButton);
        check(scene.IsAutoFit && Math.Abs(scene.ZoomPercent - fit) < 0.1 && model.PrepareSave() == original,
            "适应窗口一键恢复完整画布，布局数据保持不变");
    }
}
