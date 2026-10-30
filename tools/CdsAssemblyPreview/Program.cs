using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using xyz.Client.Presentation.Controls;
using System.Xml.Linq;
using xyz.Client.DataModels.ViewModels;
using xyz.Tools;

namespace CdsAssemblyPreview;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? Option(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        string machine = Option("--machine") ?? @"D:\Code\xyz.35021\Service\xyz.35021.Module\Config\machine.xml";
        string? output = Option("--render") ?? Option("--self-test");
        try
        {
            if (output is not null) RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
            { BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark, PrimaryColor = MaterialDesignColors.PrimaryColor.Blue, SecondaryColor = MaterialDesignColors.SecondaryColor.Lime });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml") });
            foreach (string name in new[] { "Color", "DarkColors", "FontSize", "TextBlockStyles", "BorderStyles", "TextBoxStyles", "ComboBoxStyles", "ButtonStyles", "ListBoxStyles" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/xyz.Client.Presentation;component/Styles/{name}.xaml") });
            foreach (string name in new[] { "Strings.zh-CN", "PreviewStyles", "Hardware/HardwareStrings.zh-CN", "Hardware/Pipeline/PipelineStrings.zh-CN", "Hardware/HardwareStyles" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/ToolBuilder.CdsLayout;component/Editor/{name}.xaml") });
            using var services = new ServiceCollection().AddSingleton(new PreviewViewModel(machine, Option("--module") ?? "CDS.IPA"))
                .AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<PreviewViewModel>())
                .AddSingleton<HardwareCatalogViewModel>().AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<HardwareCatalogViewModel>())
                .AddSingleton<ProcessPreviewViewModel>().AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<ProcessPreviewViewModel>())
                .AddSingleton<CdsPipelinePreviewViewModel>().AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<CdsPipelinePreviewViewModel>()).BuildServiceProvider();
            IocHelper.ServiceProvider = services;
            foreach (var vm in services.GetServices<BaseViewModel>()) vm.Init();
            var model = services.GetRequiredService<PreviewViewModel>();
            var page = new PreviewPage();
            if (output is not null)
            {
                output = Path.GetFullPath(output); Directory.CreateDirectory(output);
                using var host = new HwndSource(new HwndSourceParameters("CDS tool preview render")
                { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = 1600, Height = 940 });
                page.Width = 1600; page.Height = 940; host.RootVisual = page;
                page.Measure(new Size(1600, 940)); page.Arrange(new Rect(0, 0, 1600, 940)); page.UpdateLayout(); Pump();
                Save(page, Path.Combine(output, "cds-components.png"));
                if (Option("--self-test") is not null) SelfTest(model, page, machine, output);
                model.DoExample();
                model.Select("T1"); model.DoSimulate("supply"); model.SelectedLevel = 4;
                model.Select("T2"); model.DoSimulate("fill"); model.SelectedLevel = 2;
                model.Select("T1"); model.SelectedInspectorTab = 2; Pump(); Save(page, Path.Combine(output, "cds-example.png"));
                model.Select("F1"); Pump(); Save(page, Path.Combine(output, "cds-valve.png"));
                model.SelectPipe("p03"); model.SelectedInspectorTab = 1; Pump(); Save(page, Path.Combine(output, "cds-pipe-parameters.png"));
                page.Visibility = Visibility.Hidden; host.RootVisual = null;
                var catalog = new HardwareCatalogPage { Width = 1600, Height = 940 }; host.RootVisual = catalog;
                catalog.Measure(new Size(1600, 940)); catalog.Arrange(new Rect(0, 0, 1600, 940)); catalog.UpdateLayout(); Pump();
                if (Option("--self-test") is not null) HardwareChecks(services.GetRequiredService<HardwareCatalogViewModel>(), catalog, model, output);
                RenderHardwareCatalog(services.GetRequiredService<HardwareCatalogViewModel>(), catalog, output);
                catalog.Visibility = Visibility.Hidden; host.RootVisual = null; page.Visibility = Visibility.Visible;
                var shell = new PreviewShell(page, true) { Width = 1600, Height = 940 }; host.RootVisual = shell;
                shell.Measure(new Size(1600, 940)); shell.Arrange(new Rect(0, 0, 1600, 940)); shell.UpdateLayout(); Pump();
                Save(shell, Path.Combine(output, "hardware-shell.png"));
                shell.Pages.SelectedIndex = 0; Pump(); shell.Pages.SelectedIndex = 1; Pump();
                shell.Visibility = Visibility.Hidden; host.RootVisual = null;
                var process = new ProcessPreviewPage { Width = 1600, Height = 940 }; host.RootVisual = process;
                process.Measure(new Size(1600, 940)); process.Arrange(new Rect(0, 0, 1600, 940)); process.UpdateLayout(); Pump();
                if (Option("--self-test") is not null) ProcessChecks(services.GetRequiredService<ProcessPreviewViewModel>(), process, model, output);
                RenderProcessPreview(services.GetRequiredService<ProcessPreviewViewModel>(), process, output);
                process.Visibility = Visibility.Hidden; host.RootVisual = null;
                var pipeline = new CdsPipelinePreviewPage { Width = 1600, Height = 940 }; host.RootVisual = pipeline;
                pipeline.Measure(new Size(1600, 940)); pipeline.Arrange(new Rect(0, 0, 1600, 940)); pipeline.UpdateLayout(); Pump();
                if (Option("--self-test") is not null) PipelineChecks(services.GetRequiredService<CdsPipelinePreviewViewModel>(), pipeline, model, output);
                RenderPipelinePreview(services.GetRequiredService<CdsPipelinePreviewViewModel>(), pipeline, output);
                pipeline.Visibility = Visibility.Hidden; app.Shutdown(); return 0;
            }
            if (args.Contains("--pipeline-preview")) services.GetRequiredService<CdsPipelinePreviewViewModel>().RunCommand.Execute(null);
            var window = new Window { Title = "ToolBuilder · CDS / 基础硬件控件库", Width = 1600, Height = 940, MinWidth = 1180, MinHeight = 740,
                Background = (Brush)app.FindResource("DarkWindowBackground"), Content = new PreviewShell(page, args.Contains("--catalog"), args.Contains("--process-preview"), args.Contains("--pipeline-preview")), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            return app.Run(window);
        }
        catch (Exception ex)
        {
            string path = output is null ? Path.Combine(AppContext.BaseDirectory, "preview-error.log") : Path.Combine(output, "preview-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, ex.ToString()); return 1;
        }
    }

    private static void SelfTest(PreviewViewModel model, PreviewPage page, string machine, string output)
    {
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add("PASS " + message); }
        var original = SHA256.HashData(File.ReadAllBytes(machine));
        var xml = XDocument.Load(machine); var settings = xml.Descendants("Setting").Where(n =>
            string.Join(".", n.AncestorsAndSelf("Setting").Reverse().Select(e => (string?)e.Attribute("Name")))
            .StartsWith(model.Snapshot.ComponentRoot + ".", StringComparison.Ordinal)).ToArray();
        int Count(string type) => settings.Count(n => ((string?)n.Attribute("Type") ?? "").EndsWith("." + type, StringComparison.Ordinal));
        Check(model.TankCount == Count("TankElement") && model.ValveCount == Count("ValveElement"), "槽与独立阀门数量来自真实药液装机配置");
        Check(model.PipeCount == 0, "只有装机配置时管路为零，不由阀组数量推断");
        int installed = model.TankCount + model.ValveCount + model.SensorCount;
        Check(model.Components.Count == installed && model.ValveStates.Values.All(v => v is null), "槽、阀门、Sensor均为独立组件，初始反馈未知");
        model.DoExample(); Check(model.PipeCount == 12 && model.Components.Count(n => n.Kind != "Terminal") == installed, "示例布局定义12段管路，硬件清单仍来自装机");
        Check(model.Layout.Pipes.All(p => p.Bends.Count == 0) && page.Scene.Pipes.Values.All(p => p.Path?.Count == 2), "示例中对齐接口直接绘制一段直管");
        Check(CdsLayout.Parse(model.Layout.ToXml()).Pipes.Count == 12, "含独立阀门的预览布局保存及读取一致");
        model.Select("T1"); model.DoSimulate("supply");
        Check(model.ValveStates["CDS.IPA.Tank1.Supply.Valve1"] == true && model.ValveStates["CDS.IPA.Tank2.Supply.Valve1"] is null, "Tank1模拟不会改变Tank2阀门");
        Pump(); Check(page.Scene.Pipes["p03"].IsFlowing && !page.Scene.Pipes["p09"].IsFlowing, "供液动画按独立阀门状态刷新");
        model.Select("F1"); model.OpenValveCommand.Execute(null);
        Check(model.ValveStates["CDS.IPA.Tank1.Fill.Valve1"] == true && model.ValveStates["CDS.IPA.Tank1.Drain.Valve1"] == false, "单独操作补液阀，不改变排液阀");
        int before = model.PipeCount; model.ConnectMode = true; model.Connect("T1.Outlet"); model.Connect("T2.Inlet");
        Check(model.PipeCount == before + 1, "只有手动连接才增加管路，支持跨槽连接"); model.ConnectMode = false;
        var oldPort = model.Layout.Port("F1.Inlet"); model.Move("F1", new Point(150, 210));
        Check(model.Layout.Port("F1.Inlet") != oldPort && model.Components.Count(n => n.Kind != "Terminal") == installed, "移动阀门后连接端点跟随，硬件数量不变");
        RoutingChecks(model, page, Check);
        AlignmentChecks(model, page, output, Check);
        KeyboardChecks(model, page, Check);
        EditingChecks(model, page, output, Check);
        var stale = CdsLayout.Parse(model.Layout.ToXml());
        stale.Nodes.Add(new CdsNode("Tank", "FakeTank", "Tank99", "Tank99", 100, 100, 80, 80));
        Check(CdsLayoutComposer.Compose(model.Snapshot, stale).Nodes.All(n => n.Component != "Tank99"), "布局额外的Tank不能生成未装机硬件");
        bool rejected = false;
        try { CdsLayout.Parse("<!DOCTYPE CdsLayout [<!ENTITY x SYSTEM 'file:///missing'>]><CdsLayout Version='2'/>"); }
        catch (System.Xml.XmlException) { rejected = true; }
        Check(rejected, "布局拒绝DTD与外部实体");
        page.Visibility = Visibility.Hidden; Pump(); Check(page.Scene.Pipes.Values.All(p => !p.IsAnimationEnabled), "页面隐藏后停止全部管路动画");
        page.Visibility = Visibility.Visible;
        Check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(machine))), "预览和编辑流程未写入machine.xml");
        model.DoReset(); Check(model.PipeCount == 0 && model.Components.Count == installed, "回到装机组件视图清除布局连线，不改变硬件清单");
        File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
    }

    private static void RoutingChecks(PreviewViewModel model, PreviewPage page, Action<bool, string> check)
    {
        model.DoExample(); model.ConnectMode = true; model.Connect("IN1.Port"); model.Connect("F1.Inlet"); model.ConnectMode = false;
        var connected = model.Layout.Pipes[^1];
        check(connected.Bends.Count == 0 && page.Scene.Pipes[connected.Id].Path?.Count == 2, "新建同一水平高度的连接只生成一段直管");
        model.Select("F1"); model.PositionY += 50;
        check(model.Layout.Pipes.Single(p => p.Id == "p01").Bends.Count > 0, "接口高度不同时生成所需折线");
        var node = model.Layout.Nodes.Single(n => n.Id == "F1");
        var center = new Point(node.X + node.Width / 2, node.Y + node.Height / 2);
        page.Scene.PointerDown(center);
        page.Scene.PointerMove(new Point(center.X, -model.Layout.Port("IN1.Port").Y + 2)); page.Scene.PointerUp();
        check(Math.Abs(model.Layout.Port("F1.Inlet").Y - model.Layout.Port("IN1.Port").Y) < 0.00001
            && model.Layout.Pipes.Single(p => p.Id == "p01").Bends.Count == 0 && page.Scene.Pipes["p01"].Path?.Count == 2,
            "拖动靠近同一高度时吸附接口并清除原有拐点");
        model.PositionX += 40;
        check(model.Layout.Pipes.Single(p => p.Id == "p01").Bends.Count == 0, "水平移动只改变直管长度");
        model.Select("D1"); double drainX = model.PositionX; model.PositionX += 50; model.PositionX = drainX;
        check(new[] { "p05", "p06" }.All(id => model.Layout.Pipes.Single(p => p.Id == id).Bends.Count == 0 && page.Scene.Pipes[id].Path?.Count == 2),
            "垂直接口重新对齐后也恢复直管");
        var restored = CdsLayout.Parse(model.Layout.ToXml());
        check(restored.Pipes.Single(p => p.Id == "p01").Bends.Count == 0, "直管保存重载后不会增加拐点");
        model.SelectPipe("p01"); var pipeCenter = model.PipeCenter(model.SelectedPipe!); model.MovePipe("p01", pipeCenter + new Vector(0, 40));
        model.Select("F1"); model.Parameters.OfType<CdsTextParameter>().Single(p => p.Key == "Caption").Value = "V1";
        check(model.Layout.Pipes.Single(p => p.Id == "p01").Bends.Count == 2, "只修改文字参数时保留手动调整的管路走向");
    }

    private static void AlignmentChecks(PreviewViewModel model, PreviewPage page, string output, Action<bool, string> check)
    {
        model.DoReset(); model.ConnectMode = false;
        var tank = model.Layout.Nodes.Single(n => n.Kind == "Tank" && n.Component == "Tank1");
        var valve = model.Layout.Nodes.Single(n => n.Kind == "Valve" && n.Component == "Tank1.Fill.Valve1");
        void Drag(CdsNode node, Point position)
        {
            var point = new Point(node.X + (node.Kind == "Sensor" ? 4 : node.Width / 2), node.Y + node.Height / 2);
            page.Scene.PointerDown(point); page.Scene.PointerMove(point + (position - new Point(node.X, node.Y)));
        }
        double center = tank.X + tank.Width / 2;
        Drag(valve, new Point(center - valve.Width / 2 + 2, 90));
        var moved = model.Layout.Nodes.Single(n => n.Id == valve.Id);
        check(Math.Abs(moved.X + moved.Width / 2 - center) < 0.00001 && model.VerticalGuideX == center && model.PipeCount == 0,
            "未连接管路的控件也能吸附到另一控件的垂直中心线");
        model.SelectedInspectorTab = 0; Pump(); Save(page, Path.Combine(output, "cds-vertical-snap.png"));
        page.Scene.PointerUp(); check(model.VerticalGuideX is null, "松开拖动后清除垂直参考线");
        var sensor = model.Layout.Nodes.Single(n => n.Kind == "Sensor" && n.Component == "Tank1.H");
        Drag(sensor, new Point(tank.X + 2, 430)); page.Scene.PointerUp();
        sensor = model.Layout.Nodes.Single(n => n.Id == sensor.Id);
        check(sensor.X == tank.X && model.Layout.Nodes.Single(n => n.Id == tank.Id) == tank,
            "不同尺寸控件可左边对齐且不移动参照控件");
        Drag(sensor, new Point(tank.X + tank.Width - sensor.Width - 4, 470)); page.Scene.PointerUp();
        sensor = model.Layout.Nodes.Single(n => n.Id == sensor.Id);
        check(sensor.X + sensor.Width == tank.X + tank.Width, "不同尺寸控件可右边对齐");
        Drag(sensor, new Point(530, 470));
        check(model.Layout.Nodes.Single(n => n.Id == sensor.Id).X == 530 && model.VerticalGuideX is null,
            "离开吸附范围后恢复自由拖动"); page.Scene.PointerUp();
    }

    private static void KeyboardChecks(PreviewViewModel model, PreviewPage page, Action<bool, string> check)
    {
        model.DoExample();
        bool Press(FrameworkElement target, Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(page)!, Environment.TickCount, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(args); return args.Handled;
        }
        Point Position() => new(model.PositionX, model.PositionY);
        static bool Near(Point a, Point b) => (a - b).Length < 0.00001;
        foreach (string kind in new[] { "Tank", "Valve", "Sensor", "Terminal" })
        {
            var node = model.Layout.Nodes.First(n => n.Kind == kind); model.Select(node.Id);
            var start = Position();
            bool right = Press(page.Scene, Key.Right) && Near(Position(), start + new Vector(1, 0));
            bool down = Press(page.Scene, Key.Down) && Near(Position(), start + new Vector(1, 1));
            bool left = Press(page.Scene, Key.Left) && Near(Position(), start + new Vector(0, 1));
            bool up = Press(page.Scene, Key.Up) && Near(Position(), start);
            check(right && down && left && up && model.Selected?.Id == node.Id, kind + "通过真实方向键事件向四个方向微调1个单位");
        }
        model.Select("F1"); var valvePosition = Position();
        check(page.TryMoveSelection(Key.Right, ModifierKeys.Shift, page.Scene) && Near(Position(), valvePosition + new Vector(10, 0))
            && page.TryMoveSelection(Key.Left, ModifierKeys.Shift, page.Scene) && Near(Position(), valvePosition), "Shift加方向键按10个单位移动");
        check(Press(page.ComponentsList, Key.Right) && model.Selected?.Id == "F1" && Near(Position(), valvePosition + new Vector(1, 0)),
            "从控件列表选中后方向键移动当前控件而不切换选择");
        var beforeInput = Position(); Press(page.PositionXInput, Key.Left); Press(page.PipePicker, Key.Down);
        check(Near(Position(), beforeInput), "输入框和下拉框使用方向键时不移动画布控件");
        check(!page.TryMoveSelection(Key.Left, ModifierKeys.Control, page.Scene) && Near(Position(), beforeInput), "其他修饰键组合不会误移动控件");
        model.SelectPipe("p01"); var pipePosition = Position(); var pipe = model.SelectedPipe!;
        var ends = (model.Layout.Port(pipe.From), model.Layout.Port(pipe.To));
        bool pipeRight = Press(page.Scene, Key.Right) && Near(Position(), pipePosition + new Vector(1, 0));
        bool pipeDown = Press(page.Scene, Key.Down) && Near(Position(), pipePosition + new Vector(1, 1));
        bool pipeLeft = Press(page.Scene, Key.Left) && Near(Position(), pipePosition + new Vector(0, 1));
        bool pipeUp = Press(page.Scene, Key.Up) && Near(Position(), pipePosition);
        check(pipeRight && pipeDown && pipeLeft && pipeUp && model.Layout.Port(pipe.From) == ends.Item1 && model.Layout.Port(pipe.To) == ends.Item2,
            "管路支持四个方向键微调且保留两端组件连接");
        model.Select("IN1"); model.PositionX = 0; model.PositionY = 0; Press(page.Scene, Key.Left); Press(page.Scene, Key.Up);
        check(Position() == new Point(0, 0), "方向键移动遵守画布边界");
        model.Selected = null; model.SelectedPipeId = "";
        check(!Press(page.Scene, Key.Right) && !model.NudgeCommand.CanExecute(new Vector(1, 0)), "未选中控件时方向键不会移动其他对象");
    }

    private static void EditingChecks(PreviewViewModel model, PreviewPage page, string output, Action<bool, string> check)
    {
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        void Input(InputTextBox box, string value)
        {
            box.SetCurrentValue(TextBox.TextProperty, value);
            check(PreviewPage.CommitEditor(box), "输入提交通过：" + value); Pump();
        }
        InputTextBox Parameter(string key)
        {
            page.UpdateLayout();
            return Descendants(page.ParameterEditor).OfType<InputTextBox>().Single(input => input.DataContext is CdsEditorParameter p && p.Key == key);
        }
        model.DoExample(); model.ConnectMode = true;
        var fillValve = model.Layout.Nodes.Single(n => n.Id == "F1");
        var point = new Point(fillValve.X + fillValve.Width / 2, fillValve.Y + fillValve.Height / 2);
        check(page.Scene.HitAt(point) is not null && page.Scene.PointerDown(point), "阀门中心有可点击区域");
        check(model.IsValveSelected && model.Selected!.Id == "F1", "连线模式仍可选中阀门");
        page.Scene.PointerMove(point + new Vector(40, 10)); page.Scene.PointerUp(); Pump();
        check(model.Layout.Nodes.Single(n => n.Id == "F1").X == 150 && model.PositionY == 170, "实际拖动入口可移动阀门并同步坐标");
        model.SelectedInspectorTab = 0; Pump(); Input(page.PositionXInput, "175");
        check(model.Layout.Nodes.Single(n => n.Id == "F1").X == 175, "右侧位置输入实际应用到阀门");
        model.SelectedInspectorTab = 1; Pump(); Input(Parameter("Width"), "52"); Input(Parameter("Angle"), "90");
        var inlet = model.Layout.Port("F1.Inlet"); var outlet = model.Layout.Port("F1.Outlet");
        check(page.Scene.Valves["F1"].Width == 52 && Math.Abs(inlet.X - outlet.X) < 0.01, "阀门尺寸和旋转实际更新外观及连接口");
        model.SelectedInspectorTab = 2; Pump();
        check(page.OpenValveButton.IsEnabled && page.ValvePanel.IsVisible, "选中阀门后模拟页显示可用的开关按钮");
        Invoke(page.OpenValveButton); Pump(250);
        check(model.ValveStates["CDS.IPA.Tank1.Fill.Valve1"] == true, "真实按钮命令打开单个阀门");
        check(page.Scene.Valves["F1"].DisplayedOpening > 0.99, "阀门打开动画完成后保持打开外观");
        check(page.Scene.Pipes["p01"].IsFlowing && page.Scene.Pipes["p02"].IsFlowing, "开阀后相连管路出现流动显示");
        Invoke(page.CloseValveButton); Pump();
        check(model.ValveStates["CDS.IPA.Tank1.Fill.Valve1"] == false && !page.Scene.Pipes["p01"].IsFlowing, "关阀按钮停止对应管路流动");

        var sensor = model.Layout.Nodes.Single(n => n.Kind == "Sensor" && n.Component == "Tank1.H");
        var tankPosition = model.Layout.Nodes.Single(n => n.Id == "T1");
        var sensorPoint = new Point(sensor.X + sensor.Width / 2, sensor.Y + sensor.Height / 2);
        page.Scene.PointerDown(sensorPoint); page.Scene.PointerMove(sensorPoint + new Vector(20, 50)); page.Scene.PointerUp(); Pump();
        check(model.IsSensorSelected && model.Layout.Nodes.Single(n => n.Id == sensor.Id).Y != sensor.Y
            && model.Layout.Nodes.Single(n => n.Id == "T1") == tankPosition, "Sensor可独立选中拖动，不改变Tank位置");
        model.SelectedInspectorTab = 1; Pump(); Input(Parameter("Width"), "140");
        check(model.Layout.Nodes.Single(n => n.Id == sensor.Id).Width == 140, "Sensor也具有独立显示参数");
        model.SelectedInspectorTab = 2; Pump(); Invoke(page.SensorOnButton); Pump();
        check(model.FindSensor("Tank1.H")!.IsActive == true && model.FindSensor("Tank2.H")!.IsActive is null, "单点传感器模拟不改变其他Tank信号");

        model.ConnectMode = false;
        var firstPipe = model.Layout.Pipes.Single(p => p.Id == "p01"); var pipeStart = model.Layout.Port(firstPipe.From); var pipeEnd = model.Layout.Port(firstPipe.To);
        var firstBend = firstPipe.Bends.FirstOrDefault(new Point(pipeEnd.X, -pipeEnd.Y));
        page.Scene.PointerDown(new Point((pipeStart.X + firstBend.X) / 2, (-pipeStart.Y + firstBend.Y) / 2)); page.Scene.PointerUp(); Pump();
        check(model.SelectedPipeId == "p01" && page.PipePicker.SelectedValue as string == "p01", "画面点击管路与右侧选择同步");
        model.SelectedInspectorTab = 1; Pump(); Input(Parameter("Diameter"), "32");
        check(model.SelectedPipe!.Diameter == 32 && Math.Abs(page.Scene.Pipes["p01"].PipeRadius - 0.16) < 0.00001, "管径编辑即时改变原生管路粗细");
        Input(Parameter("Diameter"), "2");
        check(model.SelectedPipe!.WallThickness < 1 && Math.Abs(page.Scene.Pipes["p01"].PipeRadius - 0.01) < 0.00001, "缩小管径时同步约束管壁厚度");
        var diameter = Parameter("Diameter"); diameter.SetCurrentValue(TextBox.TextProperty, "-5");
        check(!PreviewPage.CommitEditor(diameter) && model.SelectedPipe.Diameter == 2, "非法管径不覆盖上次合法设置"); Input(diameter, "28");
        Input(Parameter("FlowColor"), "#33AAFF");
        check(page.Scene.Pipes["p01"].FlowColor == (Color)ColorConverter.ConvertFromString("#33AAFF"), "通用参数编辑可修改管路颜色");
        var endpoints = (model.Layout.Port("IN1.Port"), model.Layout.Port("F1.Inlet"));
        model.SelectedInspectorTab = 0; Pump(); Input(page.PositionYInput, "280");
        check(model.SelectedPipe.Bends.Count == 2 && model.Layout.Port("IN1.Port") == endpoints.Item1 && model.Layout.Port("F1.Inlet") == endpoints.Item2,
            "管路位置编辑移动中段并保留两端组件连接");
        var bend = model.SelectedPipe.Bends[0]; page.Scene.PointerDown(bend); page.Scene.PointerMove(bend + new Vector(20, -20)); page.Scene.PointerUp();
        check(model.SelectedPipe.Bends[0] != bend, "管路拐点可在画面单独拖动");
        foreach (string kind in new[] { "Tank", "Valve", "Sensor", "Terminal" })
        {
            model.Select(model.Layout.Nodes.First(n => n.Kind == kind).Id);
            check(model.Parameters.OfType<CdsNumberParameter>().Select(p => p.Key).Contains("Width")
                && model.Parameters.OfType<CdsNumberParameter>().Select(p => p.Key).Contains("Angle"), kind + "统一提供尺寸和旋转参数");
        }
        string terminalId = model.Selected!.Id;
        model.SelectedInspectorTab = 1; Pump(); Input(Parameter("Width"), "8");
        model.SelectedInspectorTab = 0; Pump(); Input(page.PositionXInput, model.Layout.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string saved = model.Layout.ToXml(); File.WriteAllText(Path.Combine(output, "edited-layout.xml"), saved);
        var restored = CdsLayout.Parse(saved); model.ApplyLayout(restored);
        check(restored.Nodes.Single(n => n.Id == "F1").Width == 52 && restored.Nodes.Single(n => n.Id == sensor.Id).Width == 140
            && restored.Pipes.Single(p => p.Id == "p01").Diameter == 28, "阀门、Sensor和管路参数完整保存重载");
        check(model.Layout.Nodes.Single(n => n.Id == terminalId).Width == 8 && model.Layout.Nodes.Single(n => n.Id == terminalId).X == model.Layout.Width,
            "外部接口尺寸与边界位置在保存重载后保持一致");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject element)
    {
        yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(element, i))) yield return child;
    }

    private static void Save(FrameworkElement page, string path)
    {
        page.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)page.Width, (int)page.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(page);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Pump(int milliseconds = 150)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
