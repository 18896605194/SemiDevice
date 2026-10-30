using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using xyz.Client.Presentation.Controls;

namespace CdsAssemblyPreview;

internal static partial class Program
{
    private static void HardwareChecks(HardwareCatalogViewModel model, HardwareCatalogPage page, PreviewViewModel cds, string output)
    {
        var checks = new List<string>();
        void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); checks.Add("PASS " + message); }
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        void Select(FluidHardwareKind kind)
        {
            model.Selected = model.Items.Single(i => i.Definition.Kind == kind);
            model.Group = model.Groups.Single(g => g.Key == model.Selected.Definition.Group); page.DetailsTabs.SelectedIndex = 0; Pump();
        }
        void Enter(InputTextBox input, string value)
        {
            input.SetCurrentValue(TextBox.TextProperty, value); Check(input.Commit(), "控件库输入提交：" + value); Pump();
        }
        string layout = cds.Layout.ToXml(); int installed = cds.Components.Count;
        byte[] machine = SHA256.HashData(File.ReadAllBytes(cds.MachinePath));
        Check(model.Count == 24 && model.Items.Select(i => i.Definition.Kind).Distinct().Count() == Enum.GetValues<FluidHardwareKind>().Length,
            "24个预览项具有独立类型且覆盖全部显示适配");
        foreach (string language in new[] { "zh-CN", "en-US" })
        {
            var dictionary = new ResourceDictionary { Source = new Uri($"pack://application:,,,/ToolBuilder.CdsLayout;component/Editor/Hardware/HardwareStrings.{language}.xaml") };
            Check(model.Items.All(i => dictionary.Contains("hw.kind." + i.Definition.Kind) && dictionary.Contains("hw.about." + i.Definition.Kind)), language + "完整提供控件名称和用途");
        }
        Select(FluidHardwareKind.FlowMeter);
        Check(model.ItemsView.Cast<HardwareDemo>().Count() == 7, "按测量分类显示7种测量和检测控件");
        var flow = model.Selected!; double? pressure = model.Items.Single(i => i.Definition.Kind == FluidHardwareKind.PressureGauge).Reading;
        Enter(page.ReadingInput, "18.75"); Check(flow.Reading == 18.75 && page.DemoVisual.ReadingText == "18.75 L/min", "流量读数与单位即时显示在真实控件上");
        Enter(page.ReadingInput, "0"); Check(page.DemoVisual.ReadingText == "0 L/min", "真实零流量与未知流量区分显示");
        Invoke(page.UnknownButton); Pump(); Check(flow.State == HardwareState.Unknown && flow.Reading is null && page.DemoVisual.ReadingText.StartsWith("—"), "未知按钮清除读数，保持未知显示");
        Enter(page.ReadingInput, "12.5"); Invoke(page.FaultButton); Pump(); Check(page.DemoVisual.State == HardwareState.Fault, "报警按钮改变所选流量计外观状态");
        Check(model.Items.Single(i => i.Definition.Kind == FluidHardwareKind.PressureGauge).Reading == pressure, "修改一个仪表不会改变另一个仪表");
        Select(FluidHardwareKind.CentrifugalPump); Invoke(page.ActiveButton); Pump();
        Check(page.DemoVisual.AnimationRunning, "泵运行按钮启动叶轮动画");
        page.Visibility = Visibility.Hidden; Pump(); Check(Descendants(page).OfType<HardwareVisual>().All(v => !v.AnimationRunning), "隐藏控件库后停止所有硬件动画");
        page.Visibility = Visibility.Visible; model.StateCommand.Execute(HardwareState.Stopped); Pump(); Check(!page.DemoVisual.AnimationRunning, "停止泵后动画停止");
        Select(FluidHardwareKind.InlineHeater); Enter(page.TargetInput, "70"); Invoke(page.ActiveButton); Pump();
        Check(page.DemoVisual.Target == 70 && page.DemoVisual.AnimationRunning, "加热器独立显示温度目标及加热动作");
        Select(FluidHardwareKind.Tank); Check(page.DemoVisual.UsesNativeTank, "储液槽直接复用平台TankVisual3D");
        Select(FluidHardwareKind.OnOffValve); Check(page.DemoVisual.UsesExistingValve, "开关阀直接复用已有ValveSymbol");
        Select(FluidHardwareKind.PhSensor); double? ph = model.Selected!.Reading;
        page.ReadingInput.SetCurrentValue(TextBox.TextProperty, "20"); Check(!page.ReadingInput.Commit() && model.Selected.Reading == ph, "仪表读数校验拒绝超出示例范围的值"); Enter(page.ReadingInput, "7.2");
        Select(FluidHardwareKind.FlowMeter); model.ResetCommand.Execute(null); var item = model.Selected!;
        Check(page.DemoPointerDown(new Point(140, 105)), "新增硬件预览具有可拖动命中区域");
        page.DemoPointerMove(new Point(150, 125)); page.DemoPointerUp(); Check(item.X == 30 && item.Y == 50, "新增硬件支持鼠标拖动");
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(page)!, Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        page.DemoVisual.RaiseEvent(key); Check(key.Handled && item.X == 31, "新增硬件支持真实方向键微调");
        page.DetailsTabs.SelectedIndex = 1; Pump(); Enter(page.SymbolWidthInput, "220"); Enter(page.SymbolAngleInput, "30");
        Check(page.DemoVisual.Width == 220 && page.DemoVisual.RenderTransform is RotateTransform rotation && rotation.Angle == 30, "新增硬件尺寸和旋转参数即时应用");
        model.Query = "Pump"; model.Group = model.Groups[0]; Check(model.ItemsView.Cast<object>().Count() == 3, "搜索可找到三类泵"); model.Query = "";
        string images = Path.Combine(output, "hardware-symbols"); Directory.CreateDirectory(images);
        foreach (var demo in model.Items)
        {
            Select(demo.Definition.Kind); model.ResetCommand.Execute(null); model.StateCommand.Execute(HardwareState.Active); Pump();
            string file = Path.Combine(images, demo.Definition.Kind + ".png"); Save(page.DemoVisual, file);
            Check(new FileInfo(file).Length > 500, "独立渲染外观：" + demo.Definition.Kind);
        }
        Check(cds.Components.Count == installed && cds.Layout.ToXml() == layout && machine.SequenceEqual(SHA256.HashData(File.ReadAllBytes(cds.MachinePath))),
            "基础控件库未增加装机硬件，未修改CDS布局或machine.xml");
        File.WriteAllLines(Path.Combine(output, "hardware-checks.txt"), checks);
    }
    private static void RenderHardwareCatalog(HardwareCatalogViewModel model, HardwareCatalogPage page, string output)
    {
        foreach (var (group, kind) in new[] { ("all", FluidHardwareKind.FlowMeter), ("delivery", FluidHardwareKind.CentrifugalPump),
            ("thermal", FluidHardwareKind.InlineHeater), ("valves", FluidHardwareKind.ProportionalValve),
            ("treatment", FluidHardwareKind.Filter), ("measurement", FluidHardwareKind.FlowMeter) })
        {
            model.Query = ""; model.Group = model.Groups.Single(g => g.Key == group);
            model.Selected = model.Items.Single(i => i.Definition.Kind == kind); model.ResetCommand.Execute(null);
            model.StateCommand.Execute(HardwareState.Active); page.DetailsTabs.SelectedIndex = 0; Pump();
            Save(page, Path.Combine(output, "hardware-" + group + ".png"));
        }
    }
}
