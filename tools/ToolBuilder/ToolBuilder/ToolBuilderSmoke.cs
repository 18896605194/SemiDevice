using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsLayoutPreview;
using ToolBuilder.Core;
using xyz.Client.Presentation.Controls.ThreeD;

namespace ToolBuilder;

internal static class ToolBuilderSmoke
{
    public static async Task Run(MainWindow window, string source, string output, string[] catalogSources)
    {
        Directory.CreateDirectory(output);
        string workspace = Path.Combine(output, "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        byte[] original = File.ReadAllBytes(source);
        string machinePath = Path.Combine(workspace, "machine.xml");
        File.WriteAllBytes(machinePath, original);
        var checks = new List<string>();
        void Check(bool value, string description)
        {
            if (!value)
            {
                throw new InvalidOperationException(description);
            }
            checks.Add("PASS " + description);
        }
        var errors = new List<string>();
        var trace = new BindingListener(errors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var vm = window.ViewModel;
        vm.OpenFile(machinePath);
        var module = vm.Roots.SelectMany(root => root.Walk()).First(node => node.Path == "CDS.IPA");
        vm.SelectNode(module);
        Check(vm.CanEditCdsLayout && vm.EditorPage == "CdsLayout" && vm.CurrentCdsLayout is not null, "选择 CDS.IPA 自动进入管路布局");
        var layout = vm.CurrentCdsLayout!;
        var editor = layout.Editor;
        Check(editor.Document.Nodes.Count(node => node.Kind == "Tank") == 2 && editor.Document.Pipes.Count == 6,
            "按 machine.xml 中 Tank1、Tank2 生成两个原生槽和六条示例管路");
        Check(editor.Viewport.Children.OfType<TankVisual3D>().Count() == 2
            && editor.Viewport.Children.OfType<FluidRouteVisual3D>().Count() == 6, "嵌入页面复用框架原生 Tank 和管路模型");
        Check(vm.Title.StartsWith("ToolBuilder") && vm.Title.EndsWith("*"), "ToolBuilder 名称和新布局未保存标记");
        Check(editor.FileActions.Visibility == Visibility.Collapsed && editor.EditorHeading.Visibility == Visibility.Collapsed, "嵌入页面使用宿主统一文件操作和标题栏");
        Check(((SolidColorBrush)editor.FindResource("DarkPrimaryText")).Color == (Color)ColorConverter.ConvertFromString("#23324A"), "画布使用 ToolBuilder 浅色主题");
        Check(!editor.Overlay.Children.OfType<TextBlock>().Any(text => text.Text is "进液口" or "供液口" or "排液口" or "供液出口"), "不显示已取消的固定接口文字");

        var tank = editor.Document.Nodes.Single(node => node.Id == "Tank2");
        Point start = new(tank.X + 80, tank.Y + 120);
        Point oldPort = editor.Document.Port("Tank2.Inlet");
        int pathCount = editor.PathUpdateCount;
        editor.PointerDown(start);
        editor.PointerMove(start + new Vector(30, 30));
        Check(editor.PathUpdateCount == pathCount, "多槽拖动期间不重建管路网格");
        editor.PointerUp();
        Check((editor.Document.Port("Tank2.Inlet") - oldPort - new Vector(30, 30)).Length < 0.001, "第二个槽和连接口同步移动");
        editor.Undo();
        Check(editor.Document.Port("Tank2.Inlet") == oldPort, "撤销恢复第二个槽");
        editor.Redo();
        var pipe = editor.Document.Pipes.First();
        editor.ObjectPicker.SelectedItem = pipe;
        editor.ViewModel.PipeProperties.Text = "IPA 补液 A&B";
        editor.ViewModel.PipeProperties.Diameter = "20";
        vm.SelectNode(module.Children.First());
        Check(!vm.CanEditCdsLayout && vm.EditorPage == "Parameters", "选中槽本身仍编辑 machine.xml 参数");
        vm.SelectNode(module);
        Check(ReferenceEquals(layout, vm.CurrentCdsLayout) && editor.ViewModel.PipeProperties.HasPendingChanges, "切换节点保留布局和未应用的属性");
        vm.SaveWorkspace(machinePath);
        string layoutPath = CdsLayoutViewModel.LayoutPath(machinePath, module.Path);
        Check(File.Exists(layoutPath) && LayoutDocument.Load(layoutPath).Pipes.First().Text == "IPA 补液 A&B"
            && LayoutDocument.Load(layoutPath).Pipes.First().Appearance.Diameter == 20, "统一保存提交文字、属性及布局 XML");
        Check(!vm.HasLayoutChanges && !vm.Title.EndsWith("*"), "保存后清除 machine.xml 和布局修改标记");
        Check(Directory.EnumerateFiles(Path.Combine(workspace, "backup"), "*.bak").Any(), "machine.xml 覆盖前创建备份");
        byte[] savedMachine = File.ReadAllBytes(machinePath);
        File.AppendAllText(layoutPath, "\n<!-- 外部修改 -->");
        ConfigDocument.SetValue(module.Element, "IsRun", "false");
        bool blocked = false;
        try
        {
            vm.SaveWorkspace(machinePath);
        }
        catch (IOException)
        {
            blocked = true;
        }
        Check(blocked && File.ReadAllBytes(machinePath).SequenceEqual(savedMachine) && vm.Document!.IsDirty,
            "布局外部修改冲突阻止整批保存，machine.xml 不被部分覆盖");
        vm.OpenFile(machinePath);
        module = vm.Roots.SelectMany(root => root.Walk()).First(node => node.Path == "CDS.IPA");
        // 不打开布局也必须随另存为复制。
        string copiedMachine = Path.Combine(workspace, "copy", "copied.xml");
        vm.SaveWorkspace(copiedMachine);
        Check(File.Exists(CdsLayoutViewModel.LayoutPath(copiedMachine, module.Path)), "machine.xml 另存为携带未打开的布局文件");
        vm.SelectNode(module);
        editor = vm.CurrentCdsLayout!.Editor;
        Check(editor.Document.Pipes.First().Text == "IPA 补液 A&B", "重新加载恢复保存的管路文字");
        editor.ObjectPicker.SelectedItem = editor.Document.Pipes.First();
        editor.ViewModel.PipeProperties.WallThickness = "999";
        vm.Validate();
        Check(vm.Issues.Any(issue => issue.Severity == "错误" && issue.Path == "CDS.IPA" && issue.Message.StartsWith("管路布局")), "统一校验显示管路属性错误并定位元件");
        blocked = false;
        try
        {
            vm.SaveWorkspace(copiedMachine);
        }
        catch (InvalidDataException)
        {
            blocked = true;
        }
        Check(blocked && editor.ViewModel.PipeProperties.HasPendingChanges, "非法管路属性阻止保存且保留编辑内容");
        editor.ViewModel.PipeProperties.WallThickness = "1.6";
        editor.ApplyPipeProperties();
        vm.SaveWorkspace(copiedMachine);
        Check(Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(CdsLayoutViewModel.LayoutPath(copiedMachine, module.Path))!, "backup"), "*.bak").Any(), "布局覆盖前创建备份");
        // 同一个文件换一种路径写法（带 .，短文件名也一样）再存：路径先规范化，打开着的布局照常覆盖，不当成"目标已存在"。
        bool sameFileSaved = true;
        try
        {
            vm.SaveWorkspace(Path.Combine(workspace, "copy", ".", "copied.xml"));
        }
        catch (IOException)
        {
            sameFileSaved = false;
        }
        Check(sameFileSaved && !vm.HasLayoutChanges, "换一种路径写法保存同一个文件，布局照常覆盖");

        string batchFirst = Path.Combine(workspace, "batch-first.xml");
        string batchSecond = Path.Combine(workspace, "batch-second.xml");
        byte[] batchBefore = System.Text.Encoding.UTF8.GetBytes("original");
        byte[] batchAfter = System.Text.Encoding.UTF8.GetBytes("changed");
        File.WriteAllBytes(batchFirst, batchBefore);
        File.WriteAllBytes(batchSecond, batchBefore);
        blocked = false;
        using (var lockedFile = new FileStream(batchSecond, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                WorkspaceSave.Write([
                    new FileWrite(batchFirst, batchAfter, SHA256.HashData(batchBefore)),
                    new FileWrite(batchSecond, batchAfter, SHA256.HashData(batchBefore))
                ]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                blocked = true;
            }
        }
        Check(blocked && File.ReadAllBytes(batchFirst).SequenceEqual(batchBefore)
            && File.ReadAllBytes(batchSecond).SequenceEqual(batchBefore), "第二个文件被占用时，已写入的第一个文件回滚");

        if (catalogSources.Length > 0)
        {
            vm.Sources.Clear();
            foreach (string catalogSource in catalogSources)
            {
                vm.Sources.Add(catalogSource);
            }
            await vm.LoadCatalogAsync();
        }
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = vm;
        using var host = new HwndSource(new HwndSourceParameters("ToolBuilder test")
        {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
            PositionX = -32000, PositionY = -32000, Width = 1600, Height = 1000
        });
        host.RootVisual = content;
        foreach (var (width, height, name) in new[] { (1600, 1000, "toolbuilder-cds.png"), (1120, 720, "toolbuilder-cds-small.png") })
        {
            if (width == 1120)
            {
                window.TreeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.TreePanel.Visibility == Visibility.Collapsed, "小窗口可收起元件树扩大画布");
            }
            content.Width = width;
            content.Height = height;
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            await Task.Delay(250);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(window.ConfigTree.Items.Count > 0 && window.CdsLayoutTab.IsSelected && editor.IsLoaded, "截图时元件树、布局页和画布已实际加载");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name));
            encoder.Save(stream);
        }
        editor.PauseAnimation();
        editor.PropertiesToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(editor.PropertiesPanel.Visibility == Visibility.Collapsed, "属性栏可收起扩大画布");
        editor.PropertiesToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(File.ReadAllBytes(source).SequenceEqual(original), "验证未修改用户原始 machine.xml");
        Check(errors.Count == 0, "无 WPF 数据绑定错误：" + string.Join("\n", errors));
        File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
        PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
    }

    private sealed class BindingListener(List<string> errors) : TraceListener
    {
        public override void Write(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                errors.Add(message);
            }
        }
        public override void WriteLine(string? message) => Write(message);
    }
}
