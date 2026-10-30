using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using ChamberLayoutEditor;
using ToolBuilder.Core;

namespace ToolBuilder;

internal static partial class ChamberLayoutSmoke
{
    public static async Task Run(MainWindow window, string source, string output, string[] catalogSources)
    {
        Directory.CreateDirectory(output);
        string workspace = Path.Combine(output, "workspace-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace);
        byte[] original = File.ReadAllBytes(source);
        string machine = Path.Combine(workspace, "machine.xml"); File.WriteAllBytes(machine, original);
        var checks = new List<string>();
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add("PASS " + message); File.WriteAllLines(Path.Combine(output, "chamber-checks.txt"), checks);
        }
        async Task Pump(int delay = 100) { await Task.Delay(delay); await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        var vm = window.ViewModel;
        vm.OpenFile(machine); vm.Sources.Clear();
        foreach (string path in catalogSources) vm.Sources.Add(path);
        await vm.LoadCatalogAsync(); await Pump();
        Check(vm.ChamberLayoutError.Length == 0, "读取腔体配置无错误：" + vm.ChamberLayoutError);
        Check(vm.Elements.Any(e => e.ChamberRole == "Chamber") && vm.Elements.Any(e => e.Interfaces?.Contains("xyz.Elements.Interfaces.INozzle") == true),
            "仅从 DLL 接口元数据识别腔体和喷嘴，不实例化后台元件");
        var chambers = vm.Roots.SelectMany(r => r.Walk()).Where(n => vm.Elements.Any(e => e.Key == n.Type && e.ChamberRole == "Chamber")).ToArray();
        Check(chambers.Length >= 2, "项目包含多个腔体");
        vm.SelectNode(chambers[0]); await Pump();
        Check(vm.CanEditChamberLayout && vm.EditorPage == "ChamberLayout", "选择腔体进入 ToolBuilder 的腔体布局 Tab");
        var editor = vm.ChamberEditor;
        var model = editor.ViewModel;
        var scene = (ChamberLayoutScene)editor.FindName("Scene");
        Check(((UIElement)editor.FindName("FileActions")).Visibility == Visibility.Collapsed
            && ((UIElement)editor.FindName("ComponentPanel")).Visibility == Visibility.Collapsed
            && ((UIElement)editor.FindName("EditorHeading")).Visibility == Visibility.Collapsed,
            "嵌入页不重复提供元件树、文件操作或独立程序标题");
        var arm = model.Parts.First(p => p.IsArm);
        string armId = arm.Id;
        var lift = model.Parts.Single(p => p.ParentId == armId && p.Kind == "Lift");
        var nozzles = model.Parts.Where(p => p.ParentId == armId && p.Kind == "Nozzle").ToArray();
        Check(nozzles.Length == 2 && model.Parts.All(p => p.Kind != "ArmAssembly"), "Arm、Lift、两路喷嘴是独立编辑项，没有摆臂总成");
        NodeModel Node(string component) => chambers[0].Walk().Single(n => n.Path == chambers[0].Path + "." + component);
        vm.SelectNode(Node(nozzles[0].Component));
        Check(model.Selected?.Id == nozzles[0].Id && model.CanSpray && !model.CanMoveArm && !model.CanRaise,
            "左侧选中喷嘴后，画布和属性选择同步，模拟动作只提供喷液");
        model.SelectedInspectorTab = 2; model.SelectedMotion!.Spray = true;
        vm.SelectNode(Node(nozzles[1].Component)); await Pump();
        Check(!model.SelectedMotion!.Spray && scene.ProbeMotion(nozzles[0].Id).Spray && !scene.ProbeMotion(nozzles[1].Id).Spray,
            "两路喷嘴分别保存和显示喷液状态");

        var content = (FrameworkElement)window.Content; window.Content = null; content.DataContext = vm;
        using var host = new HwndSource(new HwndSourceParameters("ToolBuilder chamber checks")
        { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = 1600, Height = 1000 });
        host.RootVisual = content; content.Measure(new Size(1600, 1000)); content.Arrange(new Rect(0, 0, 1600, 1000)); content.UpdateLayout(); await Pump(250);
        Check(editor.IsVisible && ((TabItem)window.FindName("ChamberLayoutTab")).IsSelected, "真实 WPF 画面显示嵌入的腔体布局页");
        await WorkspaceChecks(window, editor, scene, content, output, Check);
        vm.SelectNode(Node(arm.Component)); model.SelectedInspectorTab = 2; model.CenterCommand.Execute(null); await Pump(350);
        Check(scene.Probe(armId).CenterError < 1e-6, "独立摆臂移动到卡盘中心");
        double height = scene.Probe(armId).TipHeight;
        vm.SelectNode(Node(lift.Component)); model.RaiseCommand.Execute(null); await Pump(350);
        Check(scene.ProbeMotion(lift.Id).Raised,
            "升降气缸使用自己的动作状态");
        Check(scene.Probe(armId).TipHeight > height + 0.2 && !scene.ProbeMotion("Bowl1").Raised,
            "独立 Lift 升起带动装在它上面的 Arm 和管路，不带动 Bowl");
        model.LowerCommand.Execute(null); vm.SelectNode(Node(arm.Component)); model.HomeCommand.Execute(null); await Pump(350);
        model.SelectedInspectorTab = 0;
        var pipeBefore = scene.ProbeOrigin(nozzles[0].Id); var liftBefore = scene.ProbeOrigin(lift.Id);
        arm.X += 0.5; await Pump();
        Check(Math.Abs(scene.ProbeOrigin(nozzles[0].Id).X - pipeBefore.X - 0.5) < 1e-6
            && Math.Abs(scene.ProbeOrigin(lift.Id).X - liftBefore.X - 0.5) < 1e-6,
            "移动 Arm 安装位置时，它的 Lift 和管路按装配关系跟随");
        var otherBefore = scene.ProbeOrigin(nozzles[1].Id);
        vm.SelectNode(Node(nozzles[0].Component)); nozzles[0].Z += 0.08; await Pump();
        Check((scene.ProbeOrigin(nozzles[1].Id) - otherBefore).Length < 1e-6 && Math.Abs(arm.X - 2.45) < 1e-6,
            "单独调整一路管路相对位置不改变另一路或 Arm 的位置");
        double otherLength = scene.ProbeDimensions(nozzles[1].Id)["Length"];
        Check(!nozzles[0].Dimensions.Single(d => d.Key == "Length").IsEditable, "随摆臂计算管长时手动长度不可编辑");
        nozzles[0].AutoLength = false; nozzles[0].Dimensions.Single(d => d.Key == "Length").Value = 1.8; await Pump();
        Check(nozzles[0].Dimensions.Single(d => d.Key == "Length").IsEditable
            && Math.Abs(scene.ProbeDimensions(nozzles[0].Id)["Length"] - 1.8) < 1e-6
            && Math.Abs(scene.ProbeDimensions(nozzles[1].Id)["Length"] - otherLength) < 1e-6,
            "关闭随摆臂计算后可独立调整一路管路长度，不影响另一路");
        Capture(content, Path.Combine(output, "chamber-nozzle.png"));
        vm.SelectNode(chambers[1]); await Pump();
        Check(model.Document!.ComponentRoot == chambers[1].Path, "同一布局页切换至另一个腔体");
        vm.SelectNode(chambers[0]); await Pump();
        Check(Math.Abs(model.Parts.Single(p => p.Id == armId).X - 2.45) < 1e-6, "切换腔体后保留之前的摆放位置");

        var bowlSpec = vm.Elements.First(e => e.ChamberRole == "Bowl");
        var addedBowl = vm.CreateChild(chambers[1], "Bowl2", bowlSpec); await Pump();
        ConfigDocument.SetValue(chambers[1].Element, "Type", "B"); await Pump();
        vm.SelectNode(addedBowl); model.SelectedInspectorTab = 2; model.RaiseCommand.Execute(null); await Pump(350);
        Check(model.Document!.ChamberType == "B" && model.Parts.Count(p => p.Kind == "Bowl") == 2,
            "当前内存配置新增 Bowl2、修改 Type 后自动生成第二层 Bowl");
        Check(scene.ProbeMotion("Bowl2").Raised && !scene.ProbeMotion("Bowl1").Raised,
            "多层 Bowl 的升降状态独立，不写死第一个 Bowl");
        Check(scene.ProbeDimensions("Bowl2")["Radius"] > scene.ProbeDimensions("Bowl1")["Radius"], "多层 Bowl 默认半径不同，避免完全重叠");
        Capture(content, Path.Combine(output, "chamber-bowls.png"));
        var nozzleSpec = vm.Elements.First(e => e.ChamberRole == "Nozzle");
        vm.SelectNode(chambers[0]);
        var armNode = Node(armId);
        var newNozzle = vm.CreateChild(armNode, "Nozzle_Test", nozzleSpec); await Pump();
        Check(model.Parts.Count(p => p.Kind == "Nozzle" && p.ParentId == armId) == 3,
            "装机树增加喷嘴后布局自动增加，布局页不提供新增设备入口");
        XmlLayout.Remove(newNozzle.Element); armNode.Children.Remove(newNozzle); await Pump();
        Check(model.Parts.Count(p => p.Kind == "Nozzle" && p.ParentId == armId) == 2, "装机树删除喷嘴后布局同步移除");
        vm.SelectNode(armNode); armNode.Name = "ArmRenamed"; await Pump();
        Check(model.Parts.Single(p => p.IsArm).Id == "ArmRenamed" && model.Parts.Single(p => p.IsArm).X == 2.45
            && model.Parts.Where(p => p.Kind == "Nozzle").All(p => p.ParentId == "ArmRenamed"),
            "元件改名时保留位置并更新子元件和装配关系");
        vm.SaveWorkspace(machine); await Pump();
        string firstLayout = MainViewModel.ChamberLayoutPath(machine, chambers[0].Path);
        Check(File.Exists(firstLayout) && ChamberLayoutDocument.Parse(File.ReadAllText(firstLayout)).Parts.Any(p => p.Kind == "Arm" && p.X == 2.45),
            "ToolBuilder 统一保存 machine.xml 和腔体布局");
        Check(!vm.HasLayoutChanges, "成功保存后清除所有布局的未保存状态");
        byte[] savedMachine = File.ReadAllBytes(machine), savedLayout = File.ReadAllBytes(firstLayout);
        File.AppendAllText(firstLayout, "\n<!-- outside -->");
        model.Parts.Single(p => p.IsArm).X += 0.1;
        bool blocked = false;
        try { vm.SaveWorkspace(machine); } catch (IOException) { blocked = true; }
        Check(blocked && File.ReadAllBytes(machine).SequenceEqual(savedMachine) && vm.HasLayoutChanges,
            "布局被外部修改时不覆盖，machine.xml 不会先被写入，未保存状态保留");
        File.WriteAllBytes(firstLayout, savedLayout);
        string failedTarget = Path.Combine(workspace, "failed.xml");
        string obstacle = MainViewModel.ChamberLayoutPath(failedTarget, chambers[1].Path); Directory.CreateDirectory(obstacle);
        bool rolledBack = false;
        try { vm.SaveWorkspace(failedTarget); } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { rolledBack = true; }
        Check(rolledBack && !File.Exists(failedTarget) && !File.Exists(MainViewModel.ChamberLayoutPath(failedTarget, chambers[0].Path)) && vm.HasLayoutChanges,
            "布局写入失败时回滚本批已保存文件并保留修改");
        Directory.Delete(obstacle);
        string moved = Path.Combine(workspace, "copy.xml"); vm.SaveWorkspace(moved); await Pump();
        Check(File.Exists(MainViewModel.ChamberLayoutPath(moved, chambers[0].Path)) && File.Exists(MainViewModel.ChamberLayoutPath(moved, chambers[1].Path)),
            "另存为携带所有腔体的布局并使用新的配置文件命名空间");
        vm.OpenFile(moved); vm.Sources.Clear(); foreach (string path in catalogSources) vm.Sources.Add(path);
        await vm.LoadCatalogAsync();
        var loaded = vm.Roots.SelectMany(r => r.Walk()).First(n => n.Name == chambers[0].Name);
        vm.SelectNode(loaded); await Pump();
        Check(Math.Abs(model.Parts.Single(p => p.IsArm).X - 2.55) < 1e-6, "重新打开配置后恢复腔体布局的位置");
        Capture(content, Path.Combine(output, "chamber-restored.png"));
        Check(File.ReadAllBytes(source).SequenceEqual(original), "集成测试不修改输入 machine.xml");
        File.WriteAllLines(Path.Combine(output, "chamber-checks.txt"), checks);
        host.RootVisual = null;
    }
    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
