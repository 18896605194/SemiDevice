using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls;

namespace CdsAssemblyPreview;

internal static partial class Program
{
    private static void PipelineChecks(CdsPipelinePreviewViewModel model, CdsPipelinePreviewPage page, PreviewViewModel cds, string output)
    {
        var checks = new List<string>();
        void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); checks.Add("PASS " + message); }
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        void Input(InputTextBox box, string value) { box.SetCurrentValue(TextBox.TextProperty, value); Check(box.Commit(), "管路预览实际输入提交：" + value); Pump(); }
        string layout = cds.Layout.ToXml(); byte[] machine = SHA256.HashData(File.ReadAllBytes(cds.MachinePath)); int installed = cds.Components.Count;
        Check(model.Parts.Count == 6 && model.Parts.Count(n => n.Kind is "Pump" or "Heater") == 2, "仅在独立CDS示例管路上加入泵和加热器");
        foreach (var scene in new[] { page.Scene.PumpFront, page.Scene.HeaterFront })
        {
            Check(scene.ViewMode == HardwareViewMode.Front2D && scene.Camera.LookDirection.X == 0 && scene.Camera.LookDirection.Y == 0,
                scene.Kind + "使用固定正交正视图");
            var points = scene.Kind == FluidHardwareKind.CentrifugalPump ? new[] { new Point3D(-2.04, 0.99, 0), new Point3D(-1.21, 2.04, 0) }
                : new[] { new Point3D(-2.04, 1, 0), new Point3D(2.04, 1, 0) };
            for (int i = 0; i < points.Length; i++)
            {
                var p = scene.Hardware.Transform.Transform(points[i]); var camera = scene.Camera;
                var projection = new Point(0.5 + (p.X - camera.Position.X) / camera.Width,
                    0.5 - (p.Y - camera.Position.Y) / (camera.Width * scene.ActualHeight / scene.ActualWidth));
                Check((projection - ProcessHardwareProjection.Port(scene.Kind, i == 1)).Length < 0.000001, scene.Kind + "管口与同一三维模型正视投影重合：" + i);
            }
        }
        Invoke(page.RunButton); Pump(200);
        Check(model.IsFlowing && page.Scene.Pipes.All(p => p.IsFlowing && p.IsAnimationEnabled) && page.Scene.PumpFront.AnimationRunning && page.Scene.HeaterFront.AnimationRunning,
            "运行示例按钮启动两个显示件及整段管路流动");
        var before = model.Part("P1"); var cursor = new Point(before.X + before.Width / 2, before.Y + before.Height / 2);
        page.Scene.PointerDown(cursor); page.Scene.PointerMove(cursor + new Vector(25, -15)); page.Scene.PointerUp(); Pump();
        Check(model.Part("P1").X == before.X + 25 && model.Part("P1").Y == before.Y - 15 && page.Scene.PumpFront.ViewMode == HardwareViewMode.Front2D,
            "泵的鼠标拖动移动二维位置且不旋转视角");
        var inlet = model.Port("P1", false); var outlet = model.Port("P1", true);
        Check(page.Scene.Pipes[1].Path!.Last() == new Point3D(inlet.X / 100, -inlet.Y / 100, 0)
            && page.Scene.Pipes[2].Path!.First() == new Point3D(outlet.X / 100, -outlet.Y / 100, 0), "移动后两侧管线准确跟随泵的管口");
        foreach (var node in model.Parts.ToArray())
        {
            model.Select(node.Id); double x = model.X, y = model.Y;
            Check(page.TryMoveSelection(Key.Right, ModifierKeys.None, page.Scene.Stage) && model.X == x + 1
                && page.TryMoveSelection(Key.Up, ModifierKeys.Shift, page.Scene.Stage) && model.Y == y - 10, node.Id + "支持方向键及Shift微调");
        }
        model.Select("P1"); page.DetailsTabs.SelectedIndex = 0; Pump(); Input(page.PositionXInput, "460");
        Check(model.Part("P1").X == 460 && !page.TryMoveSelection(Key.Left, ModifierKeys.None, page.PositionXInput), "坐标输入立即移动控件，输入框保留方向键行为");
        page.DetailsTabs.SelectedIndex = 1; Pump(); Input(page.DiameterInput, "18");
        Check(page.Scene.Pipes.All(p => Math.Abs(p.PipeRadius - 0.09) < 0.000001), "管路外径编辑即时更新所有示例管段");
        page.DetailsTabs.SelectedIndex = 2; Pump(); Input(page.ReadingInput, "900");
        Check(((PumpPreviewVisual3D)page.Scene.PumpFront.Hardware).Speed == 900, "二维泵复用三维件的转速反馈");
        Invoke(page.FaultButton); Pump();
        Check(page.Scene.PumpFront.Hardware.IsError && !model.IsFlowing && page.Scene.Pipes.All(p => !p.IsFlowing), "泵故障变红并停止管路流动显示");
        Invoke(page.ActiveButton); Pump();
        model.Select("V1"); Pump(); Invoke(page.CloseValveButton); Pump();
        Check(model.InletValve == false && !model.IsFlowing, "独立关闭阀门后管路停止流动");
        var valve = model.Part("V1"); page.Scene.PointerDown(new Point(valve.X + 18, valve.Y + 18), 2); page.Scene.PointerUp(); Pump();
        Check(model.InletValve == true && model.IsFlowing, "双击管路阀门可重新打开并恢复流动");
        model.Select("H1"); page.DetailsTabs.SelectedIndex = 2; Pump(); Input(page.ReadingInput, "72");
        Check(((HeaterPreviewVisual3D)page.Scene.HeaterFront.Hardware).Temperature == 72 && model.Pump.Reading == 900, "二维加热器独立绑定温度，不改变泵的反馈");
        model.StateCommand.Execute(HardwareState.Unknown); Pump();
        Check(model.Heater.Reading is null && page.Scene.HeaterFront.State == HardwareState.Unknown, "加热器未知反馈保留null");
        model.ShowInternals = false; Pump();
        Check(!((PumpPreviewVisual3D)page.Scene.PumpFront.Hardware).ShowInternals && !((HeaterPreviewVisual3D)page.Scene.HeaterFront.Hardware).ShowInternals,
            "二维件也能切换外壳及内部结构");
        Invoke(page.RunButton); Pump(); page.Visibility = Visibility.Hidden; Pump();
        Check(page.Scene.Pipes.All(p => !p.IsAnimationEnabled) && !page.Scene.PumpFront.AnimationRunning && !page.Scene.HeaterFront.AnimationRunning,
            "离开二维管路预览后停止全部动画");
        page.Visibility = Visibility.Visible; Pump();
        Check(cds.Components.Count == installed && cds.Layout.ToXml() == layout && machine.SequenceEqual(SHA256.HashData(File.ReadAllBytes(cds.MachinePath))),
            "示例预览不增加实际装机组件，不修改装机或原CDS布局");
        File.WriteAllLines(Path.Combine(output, "pipeline-checks.txt"), checks);
    }

    private static void RenderPipelinePreview(CdsPipelinePreviewViewModel model, CdsPipelinePreviewPage page, string output)
    {
        model.ResetCommand.Execute(null); model.RunCommand.Execute(null); model.Select("H1"); page.DetailsTabs.SelectedIndex = 2;
        Pump(250); Save(page, Path.Combine(output, "cds-pipeline-running.png")); Save(page.Scene.Stage, Path.Combine(output, "cds-pipeline-diagram.png"));
        model.ShowInternals = false; model.StopCommand.Execute(null); Pump(); Save(page, Path.Combine(output, "cds-pipeline-solid.png"));
        model.Select("P1"); model.StateCommand.Execute(HardwareState.Fault); Pump(); Save(page, Path.Combine(output, "cds-pipeline-fault.png"));
    }
}
