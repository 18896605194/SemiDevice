using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using CdsAssemblyPreview;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;

namespace ToolBuilder;

internal static partial class ToolBuilderSmoke
{
    private static async Task CdsXamlChecks(CdsLayoutViewModel session, string output, Action<bool, string> check, Func<int, Task> pump)
    {
        var editor = session.Editor; var model = editor.ViewModel;
        string original = model.Layout.ToXml(), xaml = editor.PrepareXamlExport();
        var xml = XElement.Parse(xaml); XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        check(xml.Name == w + "UserControl" && !xml.Descendants(w + "ResourceDictionary").Any() && !xaml.Contains("CdsAssemblyPreview") && !xaml.Contains("ToolBuilder"),
            "CDS导出普通UserControl，只引用公共前端控件，不依赖工具或资源字典运行时");
        var reopened = CdsDiagramXaml.ReadLayout(xaml);
        check(reopened.Nodes.SequenceEqual(model.Layout.Nodes) && reopened.Pipes.Count == model.PipeCount
            && Math.Abs(reopened.Pipes[0].Diameter - model.Layout.Pipes[0].Diameter) < 1e-8 && reopened.Pipes[0].From == model.Layout.Pipes[0].From,
            "XAML可恢复所有装机组件位置、尺寸、角度以及管线连接和粗细");
        editor.ShowXaml(true); await pump(80);
        check(editor.XamlPreview.Visibility == Visibility.Visible && editor.Scene.Visibility == Visibility.Collapsed && editor.XamlPreview.Text == xaml && editor.XamlPreview.IsReadOnly,
            "CDS支持画布/XAML切换、选取复制源码，隐藏画布时停止动画");
        check(editor.SourceActions.TransformToAncestor(editor).Transform(new Point()).X >= editor.XamlPreview.ActualWidth,
            "XAML操作位于右侧，源码与画布使用同一块完整显示区域");
        check(editor.Scene.Pipes.Values.All(p => !p.IsAnimationEnabled), "查看XAML时编辑器管线停帧");
        var node = model.Layout.Nodes.First(n => n.Kind == "Valve"); model.Select(node.Id); model.PositionX += 2;
        check(editor.XamlPreview.Text != xaml && CdsDiagramXaml.ReadLayout(editor.XamlPreview.Text).Nodes.Single(n => n.Id == node.Id).X == node.X + 2,
            "调整控件位置后XAML源码同步更新");
        editor.OpenXaml(xaml); check(session.IsDirty && model.Layout.Nodes.Single(n => n.Id == node.Id).X == node.X, "打开XAML恢复布局且不会误清除未保存标记");
        XNamespace fluid = "clr-namespace:xyz.Client.Presentation.Controls.Fluid;assembly=xyz.Client.Presentation";
        XNamespace threeD = "clr-namespace:xyz.Client.Presentation.Controls.ThreeD;assembly=xyz.Client.Presentation";
        var editedSource = XElement.Parse(xaml);
        editedSource.Descendants(w + "Grid").Single(e => (string?)e.Attribute(fluid + "CdsDiagram.Id") == node.Id).SetAttributeValue("Canvas.Left", node.X + 7);
        editedSource.Descendants(threeD + "FluidRouteVisual3D").Single().SetAttributeValue("PipeRadius", .11);
        editor.OpenXaml(editedSource.ToString());
        check(model.Layout.Nodes.Single(n => n.Id == node.Id).X == node.X + 7 && Math.Abs(model.Layout.Pipes[0].Diameter - 22) < 1e-8,
            "打开人工修改的XAML时以实际Canvas位置和原生管径属性恢复布局");
        editor.OpenXaml(xaml);
        bool rejected = false;
        try { editor.OpenXaml(xaml.Replace("ComponentRoot=\"CDS.IPA\"", "ComponentRoot=\"CDS.OTHER\"")); } catch (InvalidDataException) { rejected = true; }
        check(rejected && model.Layout.ToXml() == original, "其他药液模块XAML拒绝导入并保留当前布局");
        rejected = false;
        try { editor.OpenXaml("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///invalid'>]><x>&a;</x>"); } catch (System.Xml.XmlException) { rejected = true; }
        check(rejected && model.Layout.ToXml() == original, "XAML导入禁止DTD且不执行外部内容");
        string exportPath = Path.Combine(output, "CDS.IPA.xaml"); await editor.ExportXamlAsync(exportPath);
        check(File.ReadAllText(exportPath) == xaml && session.IsDirty, "导出XAML写入独立文件，不代替顶部保存或清除修改");
        editor.ShowXaml(false); await pump(80);

        var view = (UserControl)XamlReader.Parse(xaml); view.Resources = CdsEditorTheme.CreateEmbedded(); view.DataContext = model;
        using var host = new HwndSource(new HwndSourceParameters("CDS XAML display test")
        { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = 1920, Height = 1080 });
        host.RootVisual = view;
        async Task Render(int width, int height)
        {
            view.Width = width; view.Height = height; view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout(); await pump(120);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, "cds-xaml-" + width + ".png")); encoder.Save(stream);
        }
        await Render(1280, 720);
        IEnumerable<DependencyObject> Walk(DependencyObject d)
        {
            yield return d;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(d, i))) yield return child;
        }
        var tree = Walk(view).ToArray();
        check(tree.OfType<TankVisual3D>().Count() == 2 && tree.OfType<ValveSymbol>().Count() == 6 && tree.OfType<SignalLamp>().Count() == 10,
            "导出XAML可直接由WPF加载出Tank、阀门及DI原生控件");
        var valveNode = model.Layout.Nodes.First(n => n.Kind == "Valve"); model.Select(valveNode.Id); model.CloseValveCommand.Execute(null); await pump(80);
        var valveGrid = tree.OfType<Grid>().Single(g => xyz.Client.Presentation.Controls.Fluid.CdsDiagram.GetId(g) == valveNode.Id);
        var valveVisual = valveGrid.Children.OfType<ValveSymbol>().Single();
        check(valveVisual.IsOpen == false, "项目显示控件通过ViewModel数据直接绑定到阀门依赖属性");
        model.OpenValveCommand.Execute(null); await pump(80); check(valveVisual.IsOpen == true, "ViewModel更新后导出界面的阀门开关同步变化");
        var connected = model.Layout.Nodes.Single(n => n.Id == CdsLayoutComposer.Owner(model.Layout.Pipes[0].To));
        model.Select(connected.Id); model.CloseValveCommand.Execute(null); await pump(80);
        var route = tree.OfType<FluidRouteVisual3D>().Single(); check(!route.IsFlowing && !route.IsFilled, "关闭管路所连阀门后导出界面更新停止状态");
        model.OpenValveCommand.Execute(null); await pump(80); check(route.IsFlowing && route.IsFilled && route.HasAnimatedProperties, "VM提供流动和液芯状态，导出管路直接绑定并播放动画");
        var tankNode = model.Layout.Nodes.First(n => n.Kind == "Tank"); model.Select(tankNode.Id); model.SelectedLevel = 5; await pump(80);
        check(tree.OfType<TankVisual3D>().Any(t => t.IsAlarm && Math.Abs(t.FillFraction - .865) < 1e-8), "液位和报警通过Tank依赖属性绑定更新");
        var sensorNode = model.Layout.Nodes.First(n => n.Kind == "Sensor"); model.Select(sensorNode.Id); model.SensorOffCommand.Execute(null); await pump(80);
        var sensorGrid = tree.OfType<Grid>().Single(g => xyz.Client.Presentation.Controls.Fluid.CdsDiagram.GetId(g) == sensorNode.Id);
        check(Walk(sensorGrid).OfType<SignalLamp>().Single().IsActive == false, "传感器数据直接绑定SignalLamp的IsActive");
        check(editor.PrepareXamlExport() == xaml, "模拟状态不会固化进导出XAML");
        var stage = (FrameworkElement)view.FindName("CdsStage");
        var small = stage.TransformToAncestor(view).TransformBounds(new Rect(0, 0, stage.ActualWidth, stage.ActualHeight));
        await Render(1920, 1080); var large = stage.TransformToAncestor(view).TransformBounds(new Rect(0, 0, stage.ActualWidth, stage.ActualHeight));
        check(small.Width <= 1280.1 && small.Height <= 720.1 && large.Width <= 1920.1 && large.Height <= 1080.1 && Math.Abs(large.Width / small.Width - 1.5) < .01,
            "Grid/Viewbox在1280和1920显示区域等比适配，几何无需C#运行时计算");
        view.Visibility = Visibility.Hidden; await pump(80);
        check(tree.OfType<FluidRouteVisual3D>().All(p => !p.HasAnimatedProperties), "导出界面隐藏时管线动画停止");
        host.RootVisual = null;
    }

    private static async Task CdsHardwareXamlChecks(PreviewPage editor, string output, Action<bool, string> check, Func<int, Task> pump)
    {
        var model = editor.ViewModel; string xaml = editor.PrepareXamlExport();
        var view = (UserControl)XamlReader.Parse(xaml); view.Resources = CdsEditorTheme.CreateEmbedded(); view.DataContext = model;
        using var host = new HwndSource(new HwndSourceParameters("CDS hardware XAML test")
        { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = 1600, Height = 900 });
        host.RootVisual = view; view.Width = 1600; view.Height = 900;
        view.Measure(new Size(1600, 900)); view.Arrange(new Rect(0, 0, 1600, 900)); view.UpdateLayout(); await pump(80);
        var stage = (Grid)view.FindName("CdsStage"); var canvas = stage.Children.OfType<Canvas>().Single();
        ProcessHardwareScene Visual(string id) => canvas.Children.OfType<Grid>().Single(g => xyz.Client.Presentation.Controls.Fluid.CdsDiagram.GetId(g) == id).Children.OfType<ProcessHardwareScene>().Single();
        var pumpNode = model.Components.Single(n => n.VisualKind == FluidHardwareKind.CentrifugalPump);
        var heaterNode = model.Components.Single(n => n.VisualKind == FluidHardwareKind.InlineHeater);
        model.Select(pumpNode.Id); model.HardwareStateCommand.Execute(HardwareState.Active); model.SelectedHardware!.Reading = 1450;
        model.Select(heaterNode.Id); model.HardwareStateCommand.Execute(HardwareState.Active); model.SelectedHardware!.Reading = 65; model.SelectedHardware.Target = 80; model.SelectedHardware.PowerPercent = 75; await pump(100);
        var pumpVisual = Visual(pumpNode.Id); var heaterVisual = Visual(heaterNode.Id);
        check(pumpVisual.ViewMode == HardwareViewMode.Front2D && heaterVisual.ViewMode == HardwareViewMode.Front2D
            && pumpVisual.GetType().Assembly.GetName().Name == "xyz.Client.Presentation", "导出泵和加热器使用公共前端原生控件的正交正视图");
        check(pumpVisual.State == HardwareState.Active && pumpVisual.Reading == 1450 && pumpVisual.AnimationRunning,
            "泵运行状态和转速直接绑定依赖属性并播放动画");
        check(heaterVisual.State == HardwareState.Active && heaterVisual.Reading == 65 && heaterVisual.Target == 80 && heaterVisual.PowerFraction == .75 && heaterVisual.AnimationRunning,
            "加热器温度、目标、输出及运行状态通过依赖属性绑定");
        check(editor.PrepareXamlExport() == xaml, "泵和加热器的模拟值不进入界面文件");
        var bitmap = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.Combine(output, "cds-xaml-installed-hardware-fixture.png"))) encoder.Save(stream);
        view.Visibility = Visibility.Hidden; await pump(80); check(!pumpVisual.AnimationRunning && !heaterVisual.AnimationRunning, "导出界面隐藏后泵及加热器停止动画");
        host.RootVisual = null;
    }
}
