using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using ChamberLayoutEditor;
using Microsoft.Extensions.DependencyInjection;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Manual;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using xyz.Modules;
using xyz.Service;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;
using xyz._35021.Client;
using xyz._35021.Client.Manual.Views;
using xyz._35021.Client.Presentation.Chambers;
using xyz._35021.Client.Presentation.Controls;
using ProjectViewModel = xyz._35021.Client.Manual.ViewModels.ChamberManualViewModel;

internal static class Program
{
    private static readonly List<string> Checks = [];
    private static readonly List<string> BindingErrors = [];
    private static string _output = "";
    [STAThread]
    private static int Main(string[] args)
    {
        _output = Path.GetFullPath(args[1]); Directory.CreateDirectory(_output);
        try
        {
            var app = new Application();
            PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingListener());
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
            { BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark, PrimaryColor = MaterialDesignColors.PrimaryColor.Blue, SecondaryColor = MaterialDesignColors.SecondaryColor.Lime });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml") });
            foreach (string name in new[] { "Color", "DarkColors", "FontSize", "TextBlockStyles", "BorderStyles", "TextBoxStyles", "ComboBoxStyles", "DatePickerStyles", "ButtonStyles", "CardStyles", "DataGridStyles", "ListBoxStyles", "TreeViewStyles" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/xyz.Client.Presentation;component/Styles/{name}.xaml") });
            foreach (string assembly in new[] { "xyz.Client.Presentation", "xyz.35021.Client.Presentation" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/{assembly};component/Localization/Strings.zh-CN.xaml") });
            byte[] original = File.ReadAllBytes(args[0]);
            string? Role(string type) => type.Split('.').Last() switch
            { "ChamberModule" => "Chamber", "SwingArmElement" => "Arm", "ChamberLiftElement" => "Lift", "NozzleElement" => "Nozzle", "ChamberBowlElement" => "Bowl", "ChamberDoorElement" => "Door", "SpinMotorElement" => "Disk", _ => null };
            var configuredChambers = ChamberLayoutDocument.ReadMachine(File.ReadAllText(args[0]), Role);
            var exported = configuredChambers.First();
            exported.Parts.Single(p => p.Kind == "Arm").X = 2.8;
            var saved = ChamberLayoutModel.Parse(exported.ToXml()); string root = saved.ComponentRoot;
            Check(saved.Parts.Count == exported.Parts.Count, "读取 ToolBuilder 实际导出的 Version=3 布局，不读取工具界面");
            Check(ChamberLayoutModel.Parse(exported.ToXml().Replace("Value=\"0.6\"", "Value=\"0\"")).Parts.Count > 0, "允许工具支持的零液柱长度");
            var state = State(exported);
            var composition = ChamberLayoutComposer.Compose(state, saved);
            Check(composition.Parts.Count(p => p.Kind == "Arm") == 1 && composition.Parts.Count(p => p.Kind == "Bowl") == 1
                && composition.Parts.Single(p => p.Kind == "Bowl").Id == "Bowl1", "35021 实际配置只有一条 Arm 和一个 Bowl1，按实际清单绘制");
            Check(composition.Parts.Single(p => p.Id == "Arm1").X == 2.8 && composition.Parts.Count(p => p.Kind == "Nozzle") == 2, "布局覆盖坐标并保留实际配置的两路喷嘴");
            var futureComposition = ChamberLayoutComposer.Compose(FutureState(root), saved);
            Check(futureComposition.Parts.Count(p => p.Kind == "Bowl") == 3, "通用组合器保留多 Bowl 扩展能力，此测试数据不属于 35021 装机配置");

            var provider = new TestVisualProvider(); var catalog = new ChamberVisualCatalog([provider]);
            var scene = new ChamberView { Snapshot = state, Layout = saved, VisualCatalog = catalog, Width = 1100, Height = 700 };
            using var host = new HwndSource(new HwndSourceParameters("35021 chamber checks")
            { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = 1600, Height = 1000 });
            host.RootVisual = scene; scene.Measure(new Size(1100, 700)); scene.Arrange(new Rect(0, 0, 1100, 700)); Pump();
            Check(scene.DisplayedParts.Values.Count(v => v is TestBowl) == 1, "35021 的一个 Bowl1 使用项目提供的控件");
            Check(provider.Contexts.Any(c => c.Kind == "Bowl" && c.ComponentRoot == root && c.ChamberType == saved.ChamberType), "项目提供器能按腔体类型和元件路径选择控件");
            Check(scene.DisplayedParts["Arm1"].Visual is ArmVisual3D, "项目没有 Arm 控件时由平台补齐");
            Check(((TestBowl)scene.DisplayedParts["Bowl1"]).Last.Position == CylinderPosition.Closed, "Bowl1 绑定自己的到位反馈");
            state.Bowls[0].Position = CylinderPosition.Unknown; scene.Snapshot = JsonHelper.Deserialize<ChamberDeviceDataDto>(JsonHelper.Serialize(state)); Pump();
            Check(((TestBowl)scene.DisplayedParts["Bowl1"]).Last.Position == CylinderPosition.Unknown, "Bowl1 保留气缸未知状态");
            state.Bowls[0].Position = CylinderPosition.Closed; scene.Snapshot = state; Pump();
            Check(((FluidPipeVisual3D)scene.DisplayedParts["Arm1.Nozzle_DIW"].Visual).IsFlowing
                && !((FluidPipeVisual3D)scene.DisplayedParts["Arm1.Nozzle_SC1"].Visual).IsFlowing, "DIW 与 SC1 分别绑定实际出液反馈");
            var arm = scene.DisplayedParts["Arm1"]; double radians = arm.Angle * Math.PI / 180;
            var world = ((Viewport3D)scene.FindName("Viewport")).Children[0];
            var pipeOrigin = scene.DisplayedParts["Arm1.Nozzle_DIW"].Visual.TransformToAncestor(world).Transform(new Point3D());
            var armOrigin = arm.Visual.TransformToAncestor(world).Transform(new Point3D());
            Check(Math.Abs(pipeOrigin.Y - armOrigin.Y - 0.07) < 1e-6, "喷嘴随摆臂安装，不重复叠加 Lift 的高度");
            var tip = arm.Visual.TransformToAncestor(world).Transform(new Point3D(arm.Length * Math.Cos(radians), 0, -arm.Length * Math.Sin(radians)));
            Check(Math.Sqrt(tip.X * tip.X + tip.Z * tip.Z) < 1e-5, "保存的安装坐标和真实 Reach=1 驱动臂尖到盘心");
            Check(((DiskVisual3D)scene.DisplayedParts["SpinMotor"].Visual).RotationSpeed > 0, "卡盘动画使用后台有效运动反馈");
            scene.Visibility = Visibility.Hidden; Pump();
            Check(((DiskVisual3D)scene.DisplayedParts["SpinMotor"].Visual).RotationSpeed == 0, "页面隐藏后停止显示动画");
            scene.Visibility = Visibility.Visible; Pump(); Capture(scene, "chamber-35021.png");
            scene.Layout = null; Pump(); Check(scene.DisplayedParts.Count > 0, "未保存布局时仍有正式腔体图，按实际设备生成默认排列");

            var editorServices = new ServiceCollection().AddChamberLayoutEditor().BuildServiceProvider();
            IocHelper.ServiceProvider = editorServices;
            var editor = new ChamberEditorPage(); editor.ViewModel.Init(); editor.LoadDocument(exported);
            editor.SetVisualCatalog(catalog); host.RootVisual = editor;
            editor.Measure(new Size(1100, 700)); editor.Arrange(new Rect(0, 0, 1100, 700)); Pump();
            Check(Find<BowlVisual3D>(editor).Any(b => b.BodyColor == Colors.SteelBlue), "ToolBuilder 预览同样优先使用项目控件提供器");
            host.RootVisual = null; editorServices.Dispose();

            CheckService(root, exported.ToXml());
            GrpcClientFactory.Initialize("http://127.0.0.1:1");
            var services = new ServiceCollection();
            foreach (var configured in configuredChambers) services.AddXyzChamberManual(configured.ComponentRoot.Split('.')[^1]);
            services.AddXyz35021ClientServices(); IocHelper.ServiceProvider = services.BuildServiceProvider();
            var page = IocHelper.ServiceProvider.GetRequiredKeyedService<UserControl>("Manual.Chamber1");
            Check(page is ChamberManualView && IocHelper.ServiceProvider.GetRequiredKeyedService<UserControl>("Manual.Chamber2") is ChamberManualView,
                "项目接管后台注册的菜单，不写死腔体名称和数量");
            Check(IocHelper.GetServices<BaseViewModel>().OfType<ProjectViewModel>().Count() == configuredChambers.Count, "各实际腔体 ViewModel 注册到统一初始化入口");
            var viewModel = (ProjectViewModel)page.DataContext; viewModel.UpdateDevices(state);
            var layoutService = DispatchProxy.Create<IChamberService, RpcProxy>();
            var rpc = (RpcProxy)(object)layoutService;
            rpc.Response = RpcResponse.Ok(JsonHelper.Serialize(new ChamberLayoutDto { Module = "Chamber1", ComponentRoot = root, Xml = exported.ToXml() }));
            using var testModel = new ProjectViewModel("Chamber1", new ChamberVisualCatalog(), layoutService);
            testModel.UpdateDevices(state); testModel.ReloadLayoutCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Check(testModel.Axes.Count == 2 && testModel.Cylinders.Count == 3, "实际装机快照同时更新轴和气缸操作区");
            Check(testModel.Layout?.ComponentRoot == root && testModel.LayoutStatus.Length == 0, "项目页面通过腔体接口加载布局");
            var previous = testModel.Layout; rpc.Response = RpcResponse.Ok("{\"Module\":\"Other\"}"); testModel.ReloadLayoutCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Check(ReferenceEquals(testModel.Layout, previous) && testModel.LayoutStatus.Length > 0, "布局响应异常时保留已显示布局并报告错误");
            rpc.Response = RpcResponse.Ok(JsonHelper.Serialize(new ChamberLayoutDto { Module = "Chamber1", ComponentRoot = root, Xml = exported.ToXml() }));
            testModel.ReloadLayoutCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            page.DataContext = testModel;
            scene.Width = double.NaN; scene.Height = double.NaN; host.RootVisual = page;
            page.Measure(new Size(1600, 1000)); page.Arrange(new Rect(0, 0, 1600, 1000)); Pump();
            Check(Find<ChamberView>(page).Any(v => v.IsVisible) && Find<ChamberScene>(page).All(v => !v.IsVisible), "正式项目页面显示项目腔体控件，平台默认图收起");
            Capture(page, "chamber-project-page.png"); host.RootVisual = null;
            using var defaultModel = new xyz.Client.Manual.ViewModels.ChamberManualViewModel("DefaultChamber");
            var defaultPage = new xyz.Client.Manual.Views.ChamberManualControl { ViewModel = defaultModel };
            host.RootVisual = defaultPage; defaultPage.Measure(new Size(1600, 1000)); defaultPage.Arrange(new Rect(0, 0, 1600, 1000)); Pump();
            Check(Find<ChamberScene>(defaultPage).Any(v => v.IsVisible), "未提供项目模板的页面仍显示平台默认腔体图");
            host.RootVisual = null;
            Check(BindingErrors.Count == 0, "正式页面及编辑预览无 WPF 绑定错误：" + string.Join("; ", BindingErrors.Take(3)));
            Check(File.ReadAllBytes(args[0]).SequenceEqual(original), "验证未修改项目装机配置文件");
            File.WriteAllLines(Path.Combine(_output, "checks.txt"), Checks); Console.WriteLine($"PASS {Checks.Count} checks"); return 0;
        }
        catch (Exception failure) { File.WriteAllText(Path.Combine(_output, "error.txt"), failure.ToString()); Console.Error.WriteLine(failure); return 1; }
    }
    private static void Check(bool condition, string text)
    { if (!condition) throw new InvalidOperationException(text); Checks.Add("PASS " + text); File.WriteAllLines(Path.Combine(_output, "checks.txt"), Checks); }
    private sealed class BindingListener : TraceListener
    {
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) BindingErrors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
    private static void Pump()
    { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    private static IEnumerable<T> Find<T>(DependencyObject node) where T : DependencyObject
    { if (node is T result) yield return result; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(node, i))) yield return child; }
    private static void Capture(FrameworkElement element, string name)
    { element.UpdateLayout(); var image = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(_output, name)); encoder.Save(file); }
    private static ChamberDeviceDataDto State(ChamberLayoutDocument configuration)
    {
        string root = configuration.ComponentRoot;
        var state = new ChamberDeviceDataDto { Module = root.Split('.')[^1] };
        foreach (var part in configuration.Parts)
        {
            string path = root + "." + part.Component;
            if (part.Kind == "Bowl") state.Bowls.Add(new() { Path = path, Position = CylinderPosition.Closed });
            if (part.Kind == "Door") state.Door = new() { Path = path, Position = CylinderPosition.Closed };
            if (part.Kind == "Disk") state.Spin = new() { Path = path, HasPlcData = true, IsBusy = true };
            if (part.Kind != "Arm") continue;
            var arm = new ChamberArmDto { Path = path, HasPlcData = true, Reach = 1 };
            foreach (var child in configuration.Parts.Where(p => p.ParentId == part.Id))
            {
                if (child.Kind == "Lift") arm.Lift = new() { Path = root + "." + child.Component, Position = CylinderPosition.Opened };
                if (child.Kind == "Nozzle") arm.Nozzles.Add(new() { Path = root + "." + child.Component,
                    Chemical = child.Component.Split('_')[^1], IsOn = child.Component.EndsWith("DIW", StringComparison.Ordinal) });
            }
            state.Arms.Add(arm);
        }
        return state;
    }
    private static ChamberDeviceDataDto FutureState(string root)
    {
        var state = new ChamberDeviceDataDto { Module = "Chamber1", Door = new() { Path = root + ".Door", Position = CylinderPosition.Closed }, Spin = new() { Path = root + ".SpinMotor", HasPlcData = true, IsBusy = true } };
        for (int i = 1; i <= 3; i++)
        {
            state.Bowls.Add(new() { Path = root + ".Bowl" + i, Position = i == 1 ? CylinderPosition.Closed : i == 2 ? CylinderPosition.Opened : CylinderPosition.Unknown });
            state.Arms.Add(new() { Path = root + ".Arm" + i, HasPlcData = true, Reach = i == 1 ? 1 : 0,
                Lift = new() { Path = root + ".Arm" + i + ".Lift", Position = CylinderPosition.Opened },
                Nozzles = [new() { Path = root + ".Arm" + i + ".Nozzle_DIW", Chemical = "DIW", IsOn = i == 1 }] });
        }
        return state;
    }
    private static void CheckService(string root, string xml)
    {
        string config = Path.Combine(_output, "config"); Directory.CreateDirectory(config); File.WriteAllText(Path.Combine(config, "machine.xml"), "<Settings/>");
        string paths = Path.Combine(AppContext.BaseDirectory, "Paths.json"); byte[]? previous = File.Exists(paths) ? File.ReadAllBytes(paths) : null;
        try
        {
            File.WriteAllText(paths, JsonSerializer.Serialize(new { ConfigPath = config }));
            var module = DispatchProxy.Create<IChamber, ChamberProxy>(); ((ChamberProxy)(object)module).Root = root;
            IocHelper.ServiceProvider = new ServiceCollection().AddSingleton(module).BuildServiceProvider();
            var service = new ChamberService(); var request = RpcRequest.Of((RpcParams.Module, "Chamber1"));
            var data = service.GetLayoutAsync(request).GetAwaiter().GetResult().DeserializeData<ChamberLayoutDto>();
            Check(data.Xml.Length == 0 && data.ComponentRoot == root, "后端没有布局时返回空配置及实际腔体路径");
            string folder = Path.Combine(config, "ChamberLayouts", "machine"); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, Uri.EscapeDataString(root) + ".xml"); File.WriteAllText(file, xml);
            Check(service.GetLayoutAsync(request).GetAwaiter().GetResult().DeserializeData<ChamberLayoutDto>().Xml == xml, "后端按实际模块路径读取 ToolBuilder 旁置布局");
            File.WriteAllText(file, "<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///missing'>]><ChamberLayout>&a;</ChamberLayout>");
            Check(!service.GetLayoutAsync(request).GetAwaiter().GetResult().Success, "布局不允许 DTD 或外部实体");
            Check(!service.GetLayoutAsync(RpcRequest.Of((RpcParams.Module, "../../other"))).GetAwaiter().GetResult().Success, "接口不接受任意文件路径");
            Check(!service.GetLayoutAsync(new()).GetAwaiter().GetResult().Success, "未指定腔体时不读取布局");
        }
        finally { if (previous is not null) File.WriteAllBytes(paths, previous); else File.Delete(paths); }
    }
}

public class ChamberProxy : DispatchProxy
{
    public string Root { get; set; } = "";
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch { "get_Name" => "Chamber1", "get_FullPath" => Root, _ => throw new NotSupportedException(method?.Name) };
}
public class RpcProxy : DispatchProxy
{
    public RpcResponse Response { get; set; } = RpcResponse.Ok();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Task.FromResult(Response);
}
public sealed class TestVisualProvider : IChamberVisualProvider
{
    public List<ChamberVisualContext> Contexts { get; } = [];
    public ChamberPartVisual? Create(ChamberVisualContext context)
    { Contexts.Add(context); return context.Kind == "Bowl" ? new TestBowl() : null; }
}
public sealed class TestBowl : ChamberPartVisual
{
    private readonly ChamberPartVisual _inner = new ChamberVisualCatalog().Create("Bowl");
    public override ModelVisual3D Visual => _inner.Visual;
    public ChamberVisualFeedback Last { get; private set; } = new();
    public override void Apply(IReadOnlyDictionary<string, double> sizes, ChamberVisualFeedback state)
    { Last = state; _inner.Apply(sizes, state); ((HardwareVisual3D)Visual).BodyColor = Colors.SteelBlue; }
    public override void Dispose() => _inner.Dispose();
}
