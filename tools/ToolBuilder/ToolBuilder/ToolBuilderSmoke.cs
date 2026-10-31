using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using CdsAssemblyPreview;
using ToolBuilder.Core;

namespace ToolBuilder;

internal static partial class ToolBuilderSmoke
{
    public static async Task Run(MainWindow window, string source, string output, string[] catalogSources)
    {
        Directory.CreateDirectory(output);
        string workspace = Path.Combine(output, "workspace-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace);
        byte[] original = File.ReadAllBytes(source); string machine = Path.Combine(workspace, "machine.xml"); File.WriteAllBytes(machine, original);
        var checks = new List<string>();
        void Check(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException(message);
            checks.Add("PASS " + message); File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
        }
        async Task Pump(int ms = 80) { await Task.Delay(ms); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        var errors = new List<string>(); var trace = new BindingListener(errors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(trace); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var vm = window.ViewModel; vm.OpenFile(machine);
        if (catalogSources.Length > 0) { vm.Sources.Clear(); foreach (string path in catalogSources) vm.Sources.Add(path); await vm.LoadCatalogAsync(); }
        await Pump();
        var module = vm.Roots.SelectMany(r => r.Walk()).First(n => n.Path == "CDS.IPA"); vm.SelectNode(module); await Pump();
        Check(vm.CurrentCdsLayout is not null && vm.EditorPage == "CdsLayout" && vm.LayoutError.Length == 0, "选择药液模块进入ToolBuilder的CDS布局Tab：" + vm.LayoutError);
        var session = vm.CurrentCdsLayout!; var editor = session.Editor; var model = editor.ViewModel;
        Check(model.TankCount == 2 && model.ValveCount == 6 && model.SensorCount == 10 && model.Components.Count == 18, "真实装机生成2个Tank、6个独立阀门和10个独立DI");
        Check(model.PipeCount == 0 && model.Layout.Nodes.All(n => n.Kind != "Hardware"), "当前未装泵和加热器，不生成硬件或默认管线");
        Check(editor.ExampleButton.Visibility == Visibility.Collapsed && editor.OpenLayoutButton.Visibility == Visibility.Collapsed && editor.ComponentsList.Visibility == Visibility.Collapsed,
            "嵌入页隐藏演示入口、独立文件操作和重复元件树");
        Check(session.IsDirty && vm.Title.EndsWith("*"), "新建布局进入宿主统一未保存状态");
        var content = (FrameworkElement)window.Content; window.Content = null; content.DataContext = vm;
        using var host = new HwndSource(new HwndSourceParameters("ToolBuilder CDS integration test")
        { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = 1700, Height = 1050 });
        host.RootVisual = content;
        async Task Screenshot(string name, int width = 1700, int height = 1050)
        {
            content.Width = width; content.Height = height; content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout(); await Pump(150);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
        }
        await Screenshot("toolbuilder-cds-installed.png");
        Check(editor.IsLoaded && editor.Scene.Tanks.Count == 2 && editor.Scene.Valves.Count == 6 && editor.Scene.Pipes.Count == 0, "真实WPF页面加载原生Tank及独立阀门");
        Check(editor.Scene.Scroller.ActualHeight >= editor.ActualHeight - 4
            && editor.ZoomPanel.TransformToAncestor(editor).Transform(new Point()).X > editor.Scene.ActualWidth
            && editor.EditingActions.TransformToAncestor(editor).Transform(new Point()).X > editor.Scene.ActualWidth,
            "画布独占编辑区域全高，缩放和编辑操作位于右侧属性栏");
        Check(window.SelectionHeading.Visibility == Visibility.Collapsed && editor.CanvasHeading.Visibility == Visibility.Collapsed
            && editor.CanvasNote.Visibility == Visibility.Collapsed, "CDS页收起重复标题统计和操作说明，释放画布空间");
        Check(((SolidColorBrush)editor.FindResource("DarkPrimaryText")).Color == (Color)ColorConverter.ConvertFromString("#23324A"), "CDS编辑页沿用ToolBuilder浅色主题");
        var tank = model.Components.First(n => n.Kind == "Tank"); var valve = model.Components.First(n => n.Kind == "Valve" && n.Component.Contains("Supply", StringComparison.Ordinal));
        vm.SelectNode(module.Walk().Single(n => n.Path == "CDS.IPA." + valve.Component)); await Pump();
        Check(vm.EditorPage == "CdsLayout" && model.Selected?.Component == valve.Component && model.IsValveSelected, "左侧选阀门继续显示CDS布局并同步画布选中");
        model.Select(tank.Id); await Pump(); Check(vm.Selected?.Path == "CDS.IPA." + tank.Component, "画布选中Tank同步左侧装机节点");
        model.ConnectMode = true; model.Connect(tank.Id + ".Outlet"); model.Connect(valve.Id + ".Inlet"); model.ConnectMode = false;
        Check(model.PipeCount == 1 && editor.Scene.Pipes.Count == 1, "只有手动连接才产生管路");
        model.Select(valve.Id); model.OpenValveCommand.Execute(null); await Pump();
        Check(editor.Scene.Valves[valve.Id].IsOpen == true && editor.Scene.Pipes.Values.Single().IsFlowing, "阀门模拟动作与相连管段显示独立绑定");
        await InteractionChecks(editor, Check, name => Screenshot(name));
        await CopySensorChecks(vm, module, editor, Check, name => Screenshot(name));
        await ZoomChecks(editor, Check, name => Screenshot(name));
        model.Select(tank.Id); var port = model.Layout.Port(tank.Id + ".Outlet"); double x = model.PositionX;
        Check(editor.TryMoveSelection(Key.Right, ModifierKeys.Shift, editor.Scene.Stage) && model.PositionX == x + 10, "CDS图元支持键盘方向键移动");
        Check(model.Layout.Port(tank.Id + ".Outlet").X == port.X + 10, "移动后管口与管线跟随");
        Point start = new(model.Selected!.X + 30, model.Selected.Y + 50);
        editor.Scene.PointerDown(start); editor.Scene.PointerMove(start + new Vector(30, 20)); editor.Scene.PointerUp();
        Check(model.Layout.Nodes.Single(n => n.Id == tank.Id).X > tank.X, "嵌入ToolBuilder后保留鼠标拖动");
        model.SelectedInspectorTab = 1; model.SelectPipe(model.Layout.Pipes[0].Id);
        model.Parameters.OfType<CdsNumberParameter>().Single(p => p.Key == "Diameter").Value = 20;
        Check(model.Layout.Pipes[0].Diameter == 20, "显示参数直接修改管路粗细");
        await CdsXamlChecks(session, output, Check, Pump);
        string layoutPath = CdsLayoutViewModel.LayoutPath(machine, module.Path); vm.SaveWorkspace(machine);
        Check(File.Exists(layoutPath) && CdsLayout.Parse(File.ReadAllText(layoutPath)).Nodes.Count == 18 && CdsLayout.Parse(File.ReadAllText(layoutPath)).Pipes[0].Diameter == 20,
            "顶部统一保存machine.xml和包含独立组件的Version2布局");
        Check(!vm.HasLayoutChanges && !vm.Title.EndsWith("*"), "统一保存清除未保存标记");
        string diagramPath = Path.ChangeExtension(layoutPath, ".xaml");
        Check(File.Exists(diagramPath) && CdsDiagramXaml.ReadLayout(File.ReadAllText(diagramPath)).Nodes.Count == 18, "顶部统一保存同时写入CDS显示XAML");
        byte[] originalDiagram = File.ReadAllBytes(diagramPath), originalMachine = File.ReadAllBytes(machine), originalLayout = File.ReadAllBytes(layoutPath);
        File.AppendAllText(diagramPath, "\n<!-- external XAML edit -->");
        bool xamlRejected = false; try { vm.SaveWorkspace(machine); } catch (IOException) { xamlRejected = true; }
        Check(xamlRejected && originalMachine.SequenceEqual(File.ReadAllBytes(machine)) && originalLayout.SequenceEqual(File.ReadAllBytes(layoutPath)),
            "显示XAML外部冲突阻止整批保存，machine和编辑数据保持原样");
        File.WriteAllBytes(diagramPath, originalDiagram);
        Check(Directory.EnumerateFiles(Path.Combine(workspace, "backup"), "*.bak").Any(), "统一保存保留备份机制");
        string current = model.Layout.ToXml(); byte[] disk = File.ReadAllBytes(machine);
        File.AppendAllText(layoutPath, "\n<!-- external edit -->"); ConfigDocument.SetValue(module.Element, "IsRun", "false");
        bool rejected = false; try { vm.SaveWorkspace(machine); } catch (IOException) { rejected = true; }
        Check(rejected && disk.SequenceEqual(File.ReadAllBytes(machine)), "布局外部修改冲突阻止整批保存，配置不被部分覆盖");
        vm.OpenFile(machine); module = vm.Roots.SelectMany(r => r.Walk()).First(n => n.Path == "CDS.IPA");
        string copied = Path.Combine(workspace, "copy", "copied.xml"); vm.SaveWorkspace(copied);
        Check(File.Exists(CdsLayoutViewModel.LayoutPath(copied, module.Path)), "配置另存为携带尚未打开的布局");
        Check(File.Exists(Path.ChangeExtension(CdsLayoutViewModel.LayoutPath(copied, module.Path), ".xaml")), "另存为同时携带尚未打开的CDS显示XAML");
        vm.SelectNode(module); await Pump(); session = vm.CurrentCdsLayout!; editor = session.Editor; model = editor.ViewModel;
        Check(model.Layout.ToXml() == current, "重新加载恢复组件位置和管路参数");
        model.Select(model.Components.First(n => n.Kind == "Tank").Id); string id = model.Selected!.Id; double placedX = model.PositionX;
        var tankNode = module.Children.First(n => n.Name == "Tank1"); tankNode.Name = "FeedTank"; await Pump();
        Check(model.Components.Single(n => n.Id == id).Component == "FeedTank" && model.Components.Single(n => n.Id == id).X == placedX,
            "按同一装机节点身份处理改名，位置及管线连接保留");
        tankNode.Name = "Tank1"; await Pump();
        var leakXml = new XElement("Setting", new XAttribute("Name", "LeakTray"), new XAttribute("Type", "xyz.Elements.Elements.DiSensorElement"));
        var leakNode = new NodeModel(leakXml, module); module.Element.Add(leakXml); module.Children.Add(leakNode); await Pump();
        Check(model.SensorCount == 11 && model.Components.Any(n => n.Component == "LeakTray" && n.Kind == "Sensor"), "未保存的独立漏液DI立即生成普通信号灯");
        var leak = model.Components.Single(n => n.Component == "LeakTray"); model.Select(leak.Id); model.SensorOnCommand.Execute(null);
        Check(model.FindSensor("LeakTray")?.IsActive == true, "独立漏液DI使用与HH、LL相同的触发模拟");
        Check(editor.Scene.Overlay.Children.OfType<Viewbox>().Select(v => v.Child).OfType<xyz.Client.Presentation.Controls.SignalLamp>().Any(l => l.Text == "LeakTray" && l.IsActive == true),
            "漏液实际复用SignalLamp，不绘制独立漏液硬件");
        vm.SelectNode(module); leakXml.Remove(); module.Children.Remove(leakNode); await Pump();
        Check(model.SensorCount == 10 && model.Components.All(n => n.Component != "LeakTray"), "装机删除DI后对应图元自动移除");
        var pumpXml = new XElement("Setting", new XAttribute("Name", "PumpPreviewOnly"), new XAttribute("Type", "xyz.Elements.Elements.PumpElement"));
        var heaterXml = new XElement("Setting", new XAttribute("Name", "HeaterPreviewOnly"), new XAttribute("Type", "xyz.Elements.Elements.HeaterElement"));
        // 此处只对测试副本增加工具适配器的配置夹具，不创建或调用后台类型。
        module.Element.Add(pumpXml, heaterXml); await Pump();
        Check(model.Components.Count(n => n.Kind == "Hardware") == 2 && editor.Scene.Hardware.Values.OfType<ProcessHardwareScene>().All(v => v.ViewMode == HardwareViewMode.Front2D),
            "仅在配置夹具明确包含泵及加热器时显示对应2D控件");
        await CdsHardwareXamlChecks(editor, output, Check, Pump);
        ConfigDocument.SetAttribute(pumpXml, "Type", "xyz.Elements.Elements.HeaterElement"); await Pump();
        Check(editor.Scene.Hardware.Values.OfType<ProcessHardwareScene>().Count(v => v.Kind == FluidHardwareKind.InlineHeater) == 2,
            "同一配置节点更换组件类型后，正视控件按类型同步替换");
        pumpXml.Remove(); heaterXml.Remove(); await Pump();
        Check(model.Components.All(n => n.Kind != "Hardware"), "删除配置夹具后不残留泵或加热器");
        var cloneXml = new XElement(module.Element); cloneXml.SetAttributeValue("Name", "DIW"); var parent = module.Parent!;
        var clone = new NodeModel(cloneXml, parent); parent.Element.Add(cloneXml); parent.Children.Add(clone); await Pump();
        vm.SelectNode(clone); await Pump(); var other = vm.CurrentCdsLayout!;
        Check(!ReferenceEquals(other.Editor, editor) && other.Editor.ViewModel.Snapshot.ComponentRoot == "CDS.DIW", "不同药液模块具有独立编辑会话");
        other.Editor.ViewModel.Select(other.Editor.ViewModel.Components.First(n => n.Kind == "Tank").Id); other.Editor.ViewModel.PositionX += 50;
        vm.SelectNode(module); await Pump();
        Check(ReferenceEquals(vm.CurrentCdsLayout, session) && model.Layout.Nodes.Single(n => n.Id == id).X == placedX, "切换药液模块保留各自位置，不互相覆盖");
        // 保存两份会话，验证统一保存能同时处理多个模块。
        vm.SaveWorkspace(copied);
        Check(File.Exists(CdsLayoutViewModel.LayoutPath(copied, "CDS.DIW")), "统一保存覆盖已编辑的多个药液模块");
        model.SelectPipe(model.Layout.Pipes[0].Id); var caption = model.Parameters.OfType<CdsTextParameter>().Single(p => p.Key == "WallColor"); caption.Value = "invalid-color";
        rejected = false; try { vm.SaveWorkspace(copied); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && caption.Error.Length > 0, "非法显示参数阻止保存并保留输入内容"); caption.Value = "#8291A0";
        vm.SaveWorkspace(Path.Combine(workspace, "copy", ".", "copied.xml"));
        Check(!vm.HasLayoutChanges, "同一文件的规范化路径保存可正确清除修改状态");
        // 兼容既有Version1布局，不丢弃用户明确画过的管路。
        var legacy = new CdsLayoutPreview.LayoutDocument { Id = "Legacy", Title = "Legacy", ComponentRoot = "CDS.IPA", Width = 1300, Height = 560 };
        legacy.Nodes.Add(new CdsLayoutPreview.LayoutNode { Kind = "Tank", Id = "OldTank", Component = "Tank1", Text = "Tank1", X = 220, Y = 130, Width = 160, Height = 230 });
        var migrated = CdsLayoutComposer.Compose(model.Snapshot, CdsLayout.Parse(legacy.ToXml()));
        Check(migrated.Nodes.Any(n => n.Id == "OldTank") && migrated.Nodes.Count == 18, "兼容Version1槽布局并补齐实际装机阀门及DI");
        if (catalogSources.Length > 0) { vm.Sources.Clear(); foreach (string path in catalogSources) vm.Sources.Add(path); await vm.LoadCatalogAsync(); }
        model.Select(valve.Id); model.SelectedInspectorTab = 2; await Screenshot("toolbuilder-cds-edited.png");
        Check(editor.Scene.Pipes.Values.All(p => p.IsAnimationEnabled), "可见CDS页按布局启用管线动画");
        vm.EditorPage = "Parameters"; await Pump(); Check(editor.Scene.Pipes.Values.All(p => !p.IsAnimationEnabled), "切换页签后停止CDS管线动画");
        vm.EditorPage = "CdsLayout"; await Pump();
        window.TreeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Screenshot("toolbuilder-cds-small.png", 1200, 800);
        File.WriteAllText(Path.Combine(output, "canvas-size.txt"), $"Window 1200x800; CDS viewport {editor.Scene.Scroller.ActualWidth:0}x{editor.Scene.Scroller.ActualHeight:0}; sidebar {editor.InspectorPanel.ActualWidth:0}");
        Check(window.TreePanel.Visibility == Visibility.Collapsed && editor.Scene.ActualHeight >= 500 && editor.Scene.ActualWidth >= 850
            && Math.Abs(editor.DesignRoot.ActualHeight - editor.ActualHeight) < 0.01 && editor.Scene.Scroller.ActualHeight >= editor.ActualHeight - 4,
            "收起元件树后布局区域随窗口填满可用空间");
        Check(original.SequenceEqual(File.ReadAllBytes(source)), "集成验证未修改用户原始装机配置");
        var bindingFailures = errors.Where(s => s.Contains("System.Windows.Data Error", StringComparison.Ordinal)).ToArray();
        File.WriteAllLines(Path.Combine(output, "binding-trace.txt"), errors);
        Check(bindingFailures.Length == 0, "无WPF数据绑定错误：" + string.Join("\n", bindingFailures));
        PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
    }
    private sealed class BindingListener(List<string> errors) : TraceListener
    {
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
