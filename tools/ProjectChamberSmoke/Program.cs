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
using System.Windows.Data;
using System.Xml.Linq;
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
            GrpcClientFactory.Initialize("http://127.0.0.1:1");
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
            string diagramXaml = ChamberDiagramXaml.Export(saved);
            File.WriteAllText(Path.Combine(_output, "Chamber.Chamber1.xaml"), diagramXaml);
            Check(XDocument.Parse(diagramXaml).Root!.Name.LocalName == "UserControl" && !diagramXaml.Contains("ResourceDictionary")
                && !diagramXaml.Contains("x:Key") && diagramXaml.Contains("<Grid") && diagramXaml.Contains("<Viewbox") && diagramXaml.Contains("<Viewport3D"),
                "导出为正常UserControl界面，Grid内直接声明视口和控件，界面不放在资源字典中");
            Check(diagramXaml.Contains("ArmVisual3D") && diagramXaml.Contains("BowlVisual3D") && diagramXaml.Contains("FluidPipeVisual3D") && diagramXaml.Contains("{Binding"),
                "工具导出的标准WPF XAML包含独立基础控件、装配关系和实时Binding");
            Check(ChamberDiagramXaml.ReadLayout(diagramXaml).Parts.Single(p => p.Kind == "Arm").X == 2.8,
                "标准XAML中的安装坐标可独立读取");
            string legacyXaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacyChamber.xaml"));
            var legacyContent = ChamberDiagramXaml.Load(legacyXaml);
            Check(legacyContent.View is UserControl && legacyContent.Layout.ComponentRoot == root
                && legacyContent.Parts.ContainsKey("Arm1.Nozzle_DIW") && legacyContent.Parts.ContainsKey("Bowl1"),
                "已保存的旧资源字典界面兼容读取为普通UserControl，安装数据和独立控件保留");
            var styledCatalog = new ChamberVisualCatalog([new TestVisualProvider()]);
            var styledContent = ChamberDiagramXaml.Load(ChamberDiagramXaml.Export(saved, styledCatalog));
            Check(((BowlVisual3D)styledContent.Parts["Bowl1"].Visual!).BodyColor == Colors.SteelBlue,
                "项目提供器在导出时确定控件及外观，正式显示直接加载XAML中的依赖属性");
            string projectXaml = ChamberDiagramXaml.Export(saved, new ChamberVisualCatalog([new DeclaredProjectProvider()]));
            var projectContent = ChamberDiagramXaml.Load(projectXaml);
            projectContent.View.DataContext = new { DiagramStates = saved.Parts.Where(p => p.Component.Length > 0).ToDictionary(p => p.Component,
                p => new xyz._35021.Client.Manual.Models.ChamberDiagramState { IsRaised = p.Kind == "Bowl" }), Model = new { Wafer = (object?)null } };
            Pump();
            Check(projectContent.Parts["Bowl1"].Visual is ProjectControls.ProjectBowlVisual3D projectBowl && projectBowl.IsRaised
                && BindingOperations.IsDataBound(projectBowl, ProjectControls.ProjectBowlVisual3D.IsRaisedProperty),
                "项目自有控件类型直接写入XAML并绑定自身依赖属性，正式页无需控件替换适配器");
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
            var diagramService = DispatchProxy.Create<IChamberService, RpcProxy>();
            ((RpcProxy)(object)diagramService).Response = RpcResponse.Ok(JsonHelper.Serialize(new ChamberLayoutDto
            { Module = "Chamber1", ComponentRoot = root, Xaml = diagramXaml }));
            using var diagramModel = new ProjectViewModel("Chamber1", diagramService);
            diagramModel.UpdateDevices(state); diagramModel.ReloadLayoutCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            var scene = new ChamberDiagram { DiagramXaml = diagramModel.DiagramXaml, DataContext = diagramModel, Width = 1100, Height = 700 };
            using var host = new HwndSource(new HwndSourceParameters("35021 chamber checks")
            { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = 1600, Height = 1000 });
            host.RootVisual = scene; scene.Measure(new Size(1100, 700)); scene.Arrange(new Rect(0, 0, 1100, 700)); Pump();
            Check(scene.DisplayedParts["Arm1"] is ArmVisual3D && scene.DisplayedParts["Bowl1"] is BowlVisual3D, "页面使用XAML直接声明的控件实例");
            Check(!((BowlVisual3D)scene.DisplayedParts["Bowl1"]).IsRaised, "Bowl1 绑定自己的到位反馈");
            Check(BindingOperations.IsDataBound(scene.DisplayedParts["Bowl1"], BowlVisual3D.IsRaisedProperty)
                && BindingOperations.IsDataBound(scene.DisplayedParts["Arm1"], ArmVisual3D.AngleProperty),
                "XAML直接绑定控件的IsRaised和Angle依赖属性，不经过Frame或Runtime");
            var editedXaml = XDocument.Parse(diagramXaml);
            editedXaml.Descendants().Single(e => e.Name.LocalName == "BowlVisual3D").SetAttributeValue("Radius", 1.88);
            scene.DiagramXaml = editedXaml.ToString(); Pump();
            Check(Math.Abs(((BowlVisual3D)scene.DisplayedParts["Bowl1"]).Radius - 1.88) < 1e-6,
                "直接修改XAML基础控件的尺寸属性可改变正式画面，不依赖编辑XML");
            var bowlPlacement = editedXaml.Descendants().Single(e => e.Name.LocalName == "ChamberDiagramPart3D" && (string?)e.Attribute("Id") == "Bowl1");
            bowlPlacement.Descendants().First(e => e.Name.LocalName == "TranslateTransform3D").SetAttributeValue("OffsetX", 0.23);
            scene.DiagramXaml = editedXaml.ToString(); Pump();
            Check(Math.Abs(scene.DisplayedParts["Bowl1"].TransformToAncestor(scene.Viewport!.Children[0]).Transform(new Point3D()).X - 0.23) < 1e-6,
                "实际位置直接采用XAML中的TranslateTransform3D");
            var previousParts = scene.DisplayedParts;
            scene.DiagramXaml = diagramXaml.Replace("ChamberBaseVisual3D", "ObjectDataProvider"); Pump();
            Check(ReferenceEquals(previousParts, scene.DisplayedParts) && ((TextBlock)scene.FindName("DiagramError")).Text.Length > 0,
                "非法XAML保留当前腔体显示并报告错误");
            scene.DiagramXaml = diagramXaml; Pump();
            state.Bowls[0].Position = CylinderPosition.Unknown; diagramModel.UpdateDevices(state); Pump();
            Check(((BowlVisual3D)scene.DisplayedParts["Bowl1"]).IsUnknown, "Bowl1 保留气缸未知状态");
            state.Bowls[0].Position = CylinderPosition.Closed; state.Arms[0].CurrentPosition = 37; diagramModel.UpdateDevices(state); Pump();
            Check(((FluidPipeVisual3D)scene.DisplayedParts["Arm1.Nozzle_DIW"]).IsFlowing
                && !((FluidPipeVisual3D)scene.DisplayedParts["Arm1.Nozzle_SC1"]).IsFlowing, "DIW 与 SC1 分别绑定实际出液反馈");
            var arm = (ArmVisual3D)scene.DisplayedParts["Arm1"];
            var world = scene.Viewport!.Children[0];
            var pipeOrigin = scene.DisplayedParts["Arm1.Nozzle_DIW"].TransformToAncestor(world).Transform(new Point3D());
            var armOrigin = arm.TransformToAncestor(world).Transform(new Point3D());
            Check(Math.Abs(pipeOrigin.Y - armOrigin.Y - 0.07) < 1e-6, "喷嘴随摆臂安装，不重复叠加 Lift 的高度");
            Check(arm.Angle == 37, "Angle依赖属性直接接收ViewModel发布的数据，不计算Reach或目标角度");
            var mount = ((Transform3DGroup)arm.Transform).Children.OfType<TranslateTransform3D>().First(t => BindingOperations.IsDataBound(t, TranslateTransform3D.OffsetYProperty));
            Check(Math.Abs(mount.OffsetY - ((LiftVisual3D)scene.DisplayedParts["Arm1.Lift"]).MountHeight) < 1e-6,
                "升降安装联动由XAML绑定气缸MountHeight完成");
            var diskVisual = (DiskVisual3D)scene.DisplayedParts["SpinMotor"];
            Check(diskVisual.RotationSpeed == 100 && diskVisual.HasAnimatedProperties, "卡盘依赖属性直接接收ViewModel的速度数据");
            scene.Visibility = Visibility.Hidden; Pump();
            Check(!diskVisual.HasAnimatedProperties && diskVisual.RotationSpeed == 100, "页面隐藏后控件自身停止动画，保留绑定的真实速度");
            scene.Visibility = Visibility.Visible; Pump(); Capture(scene, "chamber-35021.png");
            state.Arms[0].CurrentPosition = -65; diagramModel.UpdateDevices(state); Pump();
            var standalone = ChamberDiagramXaml.Load(diagramXaml);
            standalone.View.DataContext = diagramModel; host.RootVisual = standalone.View;
            foreach (var size in new[] { new Size(1280, 720), new Size(1920, 1080), new Size(2560, 1440), new Size(3840, 2160), new Size(1080, 1920) })
            {
                standalone.View.Measure(size); standalone.View.Arrange(new Rect(size)); standalone.View.UpdateLayout(); Pump();
                var transform = standalone.Viewport.TransformToAncestor(standalone.View);
                var bounds = transform.TransformBounds(new Rect(0, 0, standalone.Viewport.ActualWidth, standalone.Viewport.ActualHeight));
                var x = transform.Transform(new Point(1, 0)) - transform.Transform(new Point());
                var y = transform.Transform(new Point(0, 1)) - transform.Transform(new Point());
                Check(Math.Abs(standalone.View.ActualWidth - size.Width) < 1 && Math.Abs(standalone.View.ActualHeight - size.Height) < 1
                    && Math.Abs(x.Length - y.Length) < 1e-6 && bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= size.Width + 1 && bounds.Bottom <= size.Height + 1,
                    $"独立导出的界面适配{size.Width}×{size.Height}可用区域，视口保持比例并位于可用区域内");
                if (size.Width is 1280 or 1920) Capture(standalone.View, $"chamber-responsive-{size.Width}.png");
            }
            standalone.View.Measure(new Size(1024, 640)); standalone.View.Arrange(new Rect(0, 0, 1024, 640)); standalone.View.UpdateLayout();
            foreach (double dpi in new[] { 120d, 144d, 192d })
            {
                double scale = dpi / 96;
                var bitmap = new RenderTargetBitmap((int)(1024 * scale), (int)(640 * scale), dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(standalone.View);
                Check(bitmap.PixelWidth == (int)(1024 * scale) && bitmap.PixelHeight == (int)(640 * scale), $"界面在{dpi / 96:P0}显示缩放下按WPF逻辑单位呈现");
            }
            host.RootVisual = scene;
            using var defaultDiagramModel = new ProjectViewModel("Chamber1"); defaultDiagramModel.UpdateDevices(state);
            Check(ChamberDiagramXaml.Load(defaultDiagramModel.DiagramXaml).Parts.Count > 0, "未保存布局时按实际设备生成默认XAML");

            var editorServices = new ServiceCollection().AddChamberLayoutEditor().BuildServiceProvider();
            IocHelper.ServiceProvider = editorServices;
            var editor = new ChamberEditorPage(); editor.ViewModel.Init(); editor.LoadDocument(exported);
            editor.SetVisualCatalog(catalog); host.RootVisual = editor;
            editor.Measure(new Size(1100, 700)); editor.Arrange(new Rect(0, 0, 1100, 700)); Pump();
            Check(Find<BowlVisual3D>(editor).Any(b => b.BodyColor == Colors.SteelBlue), "ToolBuilder 预览同样优先使用项目控件提供器");
            host.RootVisual = null; editorServices.Dispose();

            CheckService(root, exported.ToXml(), diagramXaml, legacyXaml);
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
            rpc.Response = RpcResponse.Ok(JsonHelper.Serialize(new ChamberLayoutDto { Module = "Chamber1", ComponentRoot = root, Xml = exported.ToXml(), Xaml = diagramXaml }));
            using var testModel = new ProjectViewModel("Chamber1", layoutService);
            testModel.UpdateDevices(state); testModel.ReloadLayoutCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Check(testModel.Axes.Count == 2 && testModel.Cylinders.Count == 3, "实际装机快照同时更新轴和气缸操作区");
            Check(testModel.Layout?.ComponentRoot == root && testModel.LayoutStatus.Length == 0, "项目页面通过腔体接口加载布局");
            Check(testModel.DiagramXaml == diagramXaml, "项目ViewModel通过原有腔体接口取得标准XAML");
            var previous = testModel.Layout; rpc.Response = RpcResponse.Ok("{\"Module\":\"Other\"}"); testModel.ReloadLayoutCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Check(ReferenceEquals(testModel.Layout, previous) && testModel.LayoutStatus.Length > 0, "布局响应异常时保留已显示布局并报告错误");
            rpc.Response = RpcResponse.Ok(JsonHelper.Serialize(new ChamberLayoutDto { Module = "Chamber1", ComponentRoot = root, Xml = exported.ToXml(), Xaml = diagramXaml }));
            testModel.ReloadLayoutCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            page.DataContext = testModel;
            scene.Width = double.NaN; scene.Height = double.NaN; host.RootVisual = page;
            page.Measure(new Size(1600, 1000)); page.Arrange(new Rect(0, 0, 1600, 1000)); Pump();
            Check(Find<ChamberDiagram>(page).Any(v => v.IsVisible && v.DiagramXaml == diagramXaml) && Find<ChamberScene>(page).All(v => !v.IsVisible), "手动页面XAML直接嵌入ChamberDiagram并加载工具生成的XAML");
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
            if (part.Kind == "Disk") state.Spin = new() { Path = path, HasPlcData = true, IsBusy = true, CurrentSpeed = 100 };
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
    private static void CheckService(string root, string xml, string xaml, string legacyXaml)
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
            string diagramFile = Path.ChangeExtension(file, ".xaml"); File.WriteAllText(diagramFile, xaml);
            Check(service.GetLayoutAsync(request).GetAwaiter().GetResult().DeserializeData<ChamberLayoutDto>().Xaml == xaml,
                "后端从同一配置目录返回腔体XAML，不执行界面代码");
            File.WriteAllText(diagramFile, legacyXaml);
            Check(service.GetLayoutAsync(request).GetAwaiter().GetResult().DeserializeData<ChamberLayoutDto>().Xaml == legacyXaml,
                "布局接口继续兼容已保存的旧版XAML，工具下次导出使用普通UserControl");
            File.WriteAllText(diagramFile, xaml);
            File.Delete(file);
            Check(service.GetLayoutAsync(request).GetAwaiter().GetResult().DeserializeData<ChamberLayoutDto>().Xaml == xaml,
                "只有导出的XAML也能供正式页面读取，不依赖工具编辑XML");
            File.WriteAllText(file, xml); File.Delete(diagramFile);
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
public sealed class DeclaredProjectProvider : IChamberVisualProvider
{
    public ChamberPartVisual? Create(ChamberVisualContext context) => context.Kind == "Bowl" ? new DeclaredProjectBowl() : null;
}
public sealed class DeclaredProjectBowl : ChamberPartVisual
{
    private readonly ProjectControls.ProjectBowlVisual3D _visual = new();
    public override ModelVisual3D Visual => _visual;
    public override void Apply(IReadOnlyDictionary<string, double> sizes, ChamberVisualFeedback feedback) => _visual.Radius = sizes["Radius"];
}
