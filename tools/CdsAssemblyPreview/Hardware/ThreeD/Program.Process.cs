using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;

namespace CdsAssemblyPreview;

internal static partial class Program
{
    private static void ProcessChecks(ProcessPreviewViewModel model, ProcessPreviewPage page, PreviewViewModel cds, string output)
    {
        var checks = new List<string>();
        void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); checks.Add("PASS " + message); }
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        void Input(InputTextBox box, string value) { box.SetCurrentValue(TextBox.TextProperty, value); Check(box.Commit(), "三维预览参数提交：" + value); Pump(); }
        byte[] machine = SHA256.HashData(File.ReadAllBytes(cds.MachinePath)); string layout = cds.Layout.ToXml();
        Check(page.PumpScene.Hardware is PumpPreviewVisual3D && page.HeaterScene.Hardware is HeaterPreviewVisual3D
            && page.PumpScene.Hardware is HardwareVisual3D && page.HeaterScene.Hardware is HardwareVisual3D, "泵与加热器均继承平台硬件基类");
        foreach (var scene in new[] { page.PumpScene, page.HeaterScene })
        {
            var geometries = ((Model3DGroup)scene.Hardware.Content).Children.OfType<GeometryModel3D>().Select(m => (MeshGeometry3D)m.Geometry).ToArray();
            Check(geometries.Length > 10 && geometries.All(m => m.Positions.Count > 0 && m.Positions.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z))
                && m.Normals.All(n => double.IsFinite(n.X) && double.IsFinite(n.Y) && double.IsFinite(n.Z))
                && m.TriangleIndices.All(i => i >= 0 && i < m.Positions.Count)), "实体三维网格有效：" + scene.Hardware.GetType().Name);
        }
        Input(page.PumpSpeedInput, "900"); Invoke(page.PumpRunButton); Pump(250);
        var pump = (PumpPreviewVisual3D)page.PumpScene.Hardware; double before = pump.RotorAngle; Pump(160);
        Check(pump.IsRunning && pump.Speed == 900 && page.PumpScene.AnimationRunning && pump.RotorAngle != before, "运行按钮驱动三维叶轮，转速反馈独立绑定");
        var firstMesh = ((Model3DGroup)pump.Content).Children.OfType<GeometryModel3D>().First();
        Color active = ((SolidColorBrush)((DiffuseMaterial)firstMesh.Material).Brush).Color;
        Check(active.B > active.R, "运行反馈使用与平台一致的蓝色动作高亮");
        Invoke(page.PumpStopButton); Pump(); before = pump.RotorAngle; Pump(); Check(!page.PumpScene.AnimationRunning && pump.RotorAngle == before, "停止后叶轮停在当前位置");
        Invoke(page.PumpFaultButton); Pump(); Color fault = ((SolidColorBrush)((DiffuseMaterial)firstMesh.Material).Brush).Color;
        Check(pump.IsError && fault.R > fault.B && !page.PumpScene.AnimationRunning, "故障反馈使用平台报警红并停止动作");
        double yaw = page.PumpScene.Yaw; page.PumpScene.RotateView(new Vector(50, -25)); Check(page.PumpScene.Yaw != yaw, "鼠标旋转入口改变三维视角");
        page.PumpScene.ZoomView(120); Check(page.PumpScene.ZoomLevel > 1, "滚轮入口放大三维图");
        Invoke(page.PumpResetButton); Pump(); Check(page.PumpScene.Yaw == -28 && page.PumpScene.ZoomLevel == 1, "默认视角按钮恢复视角和缩放");
        Input(page.HeaterTargetInput, "75"); Input(page.HeaterPowerInput, "80"); Invoke(page.HeaterRunButton); Pump(200);
        var heater = (HeaterPreviewVisual3D)page.HeaterScene.Hardware; double phase = heater.HeatPhase; Pump();
        Check(heater.IsHeating && heater.TargetTemperature == 75 && heater.PowerFraction == 0.8 && heater.HeatPhase != phase, "加热按钮驱动三维加热芯反馈，温度目标与输出独立绑定");
        Input(page.HeaterPowerInput, "0"); phase = heater.HeatPhase; Pump(); Check(!page.HeaterScene.AnimationRunning && heater.HeatPhase == phase, "零输出时停止加热芯动画");
        model.ShowInternals = false; Pump(); Check(!pump.ShowInternals && !heater.ShowInternals, "内部结构开关同时切换两个控件的外壳显示");
        model.ShowInternals = true;
        Invoke(page.LeakOffButton); Pump(); Check(page.LeakLamp.IsActive == false && page.LeakLamp.IsAlarm, "漏液未触发为普通DI灭灯");
        Invoke(page.LeakOnButton); Pump(); Check(page.LeakLamp.IsActive == true && page.LeakLamp.IsAlarm, "漏液触发使用HH同款DI报警灯");
        Invoke(page.LeakUnknownButton); Pump(); Check(page.LeakLamp.IsActive is null, "漏液未知反馈保留null而非false");
        var lamp = new HardwareVisual { Kind = FluidHardwareKind.LeakSensor, State = HardwareState.Stopped };
        Check(lamp.UsesSignalLamp && lamp.DiActive == false, "控件库漏液项直接复用SignalLamp"); lamp.State = HardwareState.Fault;
        Check(lamp.DiActive == true, "控件库DI报警状态映射为触发反馈");
        model.PumpStateCommand.Execute(HardwareState.Active); model.HeaterStateCommand.Execute(HardwareState.Active); model.PowerPercent = 65; Pump();
        page.Visibility = Visibility.Hidden; Pump(); Check(!page.PumpScene.AnimationRunning && !page.HeaterScene.AnimationRunning, "切换或隐藏预览页停止两个三维件的所有动画");
        page.Visibility = Visibility.Visible;
        Check(layout == cds.Layout.ToXml() && machine.SequenceEqual(SHA256.HashData(File.ReadAllBytes(cds.MachinePath))), "新三维预览未修改装机配置与CDS布局");
        File.WriteAllLines(Path.Combine(output, "process-checks.txt"), checks);
    }
    private static void RenderProcessPreview(ProcessPreviewViewModel model, ProcessPreviewPage page, string output)
    {
        page.PumpScene.ResetCamera(); page.HeaterScene.ResetCamera();
        model.ShowInternals = false; model.PumpStateCommand.Execute(HardwareState.Stopped); model.HeaterStateCommand.Execute(HardwareState.Stopped); model.LeakStateCommand.Execute(HardwareState.Stopped);
        Pump(); Save(page, Path.Combine(output, "process-solid.png"));
        model.ShowInternals = true; model.PowerPercent = 65; model.PumpStateCommand.Execute(HardwareState.Active); model.HeaterStateCommand.Execute(HardwareState.Active);
        Pump(250); Save(page, Path.Combine(output, "process-running.png"));
        model.PumpStateCommand.Execute(HardwareState.Fault); model.HeaterStateCommand.Execute(HardwareState.Fault); model.LeakStateCommand.Execute(HardwareState.Fault);
        Pump(); Save(page, Path.Combine(output, "process-fault.png"));
    }
}
