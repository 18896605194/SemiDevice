using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using ToolBuilder.Core;

namespace ToolBuilder;

internal static class ConfigEditorSmoke
{
    public static async Task Run(MainWindow window, string source, string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
            checks.Add("PASS " + message);
        }
        byte[] before = File.ReadAllBytes(source);
        var vm = window.ViewModel;
        vm.OpenFile(source);
        Check(vm.Sources.Count > 0, "源码目录 machine.xml 自动发现元件来源，无需手动指定");
        await vm.LoadCatalogAsync();
        Check(vm.Elements.Count >= 70, $"自动识别完整元件目录：{vm.Elements.Count} 个元件");
        Check(vm.Elements.Any(spec => spec.ShortName == "TankElement"), "元件选择列表包含 TankElement");
        var component = vm.Elements.First(spec => spec.ShortName == "TankElement");
        var pickerModel = new ElementPickerViewModel([], "machine.xml 根目录");
        Check(pickerModel.IsEmpty && !pickerModel.ConfirmCommand.CanExecute(null), "无 DLL 时显示明确空状态且不能提交空元件");
        pickerModel.ReplaceElements(vm.Elements);
        Check(!pickerModel.IsEmpty, "加载来源后选择列表自动更新");
        pickerModel.Search = "no-such-component-123";
        Check(pickerModel.IsEmpty && pickerModel.EmptyMessage.Contains("搜索"), "无搜索结果与无元件来源分别提示");

        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = vm;
        using var host = new HwndSource(new HwndSourceParameters("Config editor regression")
        {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
            PositionX = -32000, PositionY = -32000, Width = 1440, Height = 940
        });
        host.RootVisual = content;
        async Task Layout()
        {
            content.Width = 1440;
            content.Height = 940;
            content.Measure(new Size(1440, 940));
            content.Arrange(new Rect(0, 0, 1440, 940));
            content.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
        vm.SelectNode(vm.Roots.SelectMany(root => root.Walk()).First(node => node.Path == "Safety.Emo"));
        await Layout();
        var boolField = vm.Fields.Single(field => field.Name == "TriggerValue");
        var boolCombo = Descendants(window.FieldsGrid).OfType<ComboBox>()
            .Single(combo => combo.DataContext == boolField && combo.Visibility == Visibility.Visible);
        Check(!boolCombo.IsEditable && boolCombo.Items.Cast<string>().SequenceEqual(new[] { "true", "false" }), "machine.xml 布尔参数仅允许 true/false 下拉选择");
        Check(!vm.Document!.IsDirty, "加载元数据、显示下拉控件不会写入默认值或弄脏 machine.xml");
        boolCombo.SelectedItem = "true";
        Check(ConfigDocument.Value(boolField.Node, boolField.Name) == "true", "下拉选择实际写入 true");
        boolCombo.SelectedItem = "false";
        Check(ConfigDocument.Value(boolField.Node, boolField.Name) == "false", "下拉选择实际写入 false");
        var bitmap = new RenderTargetBitmap(1440, 940, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, "boolean-and-root.png")))
        {
            encoder.Save(stream);
        }

        string emptyPath = Path.Combine(output, "empty.xml");
        File.WriteAllText(emptyPath, "<ArrayOfSetting />");
        vm.OpenFile(emptyPath);
        await Layout();
        Check(window.AddRootButton.IsEnabled && window.AddRootGroupButton.IsEnabled && vm.Roots.Count == 0,
            "空 machine.xml 无选中节点时，根元件和根分组按钮均可用");
        window.ConfigTree.ContextMenu.PlacementTarget = window.ConfigTree;
        window.ConfigTree.ContextMenu.IsOpen = true;
        await Layout();
        Check(((MenuItem)window.ConfigTree.ContextMenu.Items[0]).Command == vm.AddRootCommand,
            "空白树右键菜单绑定根元件命令");
        window.ConfigTree.ContextMenu.IsOpen = false;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.Show();
        window.Hide();

        // 通过真实按钮命令打开模态窗口并确认，防止只验证模型而遗漏 UI 入口。
        _ = window.Dispatcher.BeginInvoke(() =>
        {
            var picker = Application.Current.Windows.OfType<ElementPicker>().Single();
            Check(picker.ViewModel.ParentPath == "machine.xml 根目录" && picker.ViewModel.IsGroup, "根分组入口打开正确类别与目标");
            picker.ViewModel.ConfirmCommand.Execute(null);
        }, DispatcherPriority.ApplicationIdle);
        window.AddRootGroupButton.Command.Execute(null);
        Check(vm.Roots.Count == 1 && vm.Roots[0].Parent is null, "没有 DLL 也能通过界面添加根分组");
        vm.Elements.Add(component);
        _ = window.Dispatcher.BeginInvoke(() =>
        {
            var picker = Application.Current.Windows.OfType<ElementPicker>().Single();
            Check(picker.ViewModel.ParentPath == "machine.xml 根目录", "已选中分组时根元件入口仍指向 machine.xml 根目录");
            picker.ViewModel.SelectedElement = picker.ViewModel.Elements.Single();
            picker.ViewModel.ConfirmCommand.Execute(null);
        }, DispatcherPriority.ApplicationIdle);
        window.AddRootButton.Command.Execute(null);
        Check(vm.Roots.Count == 2 && vm.Roots[1].Parent is null && vm.Roots[1].Type == component.Key,
            "根元件添加在最外层，不会误加到当前选中分组内");
        vm.Document!.Save(emptyPath);
        Check(ConfigDocument.Open(emptyPath).Xml.Root!.Elements("Setting").Count() == 2, "根元件与根分组保存后可重新读取");

        string fallbackPath = Path.Combine(output, "fallback.xml");
        File.WriteAllText(fallbackPath, "<ArrayOfSetting><Setting Name=\"Unknown\"><Value Name=\"Flag\" Value=\"False\" /></Setting></ArrayOfSetting>");
        vm.OpenFile(fallbackPath);
        await Layout();
        Check(vm.Fields.Single().IsBoolean && vm.Fields.Single().BooleanValue == "false" && !vm.Document!.IsDirty,
            "没有元数据的布尔字面量也使用下拉，保留原有大小写直至用户编辑");
        var invalid = new FieldModel(new XElement("Setting"), new PropertySpec("Flag", "System.Boolean", "invalid", "", "", false, ["true", "false"]));
        invalid.BooleanValue = null;
        Check(invalid.Value == "invalid" && invalid.Error is not null, "无效布尔值不会被下拉初始化悄悄覆盖");

        var row = new RecipeFieldModel(new NodeModel(new XElement("Setting", new XAttribute("Name", "Flag"))), false, [""]);
        row.Type = "Bool";
        var presenter = new ContentPresenter { Content = row, ContentTemplate = (DataTemplate)Application.Current.FindResource("RecipeDefaultEditor") };
        presenter.ApplyTemplate();
        presenter.Measure(new Size(100, 60));
        var defaultCombo = Descendants(presenter).OfType<ComboBox>().Single();
        Check(!defaultCombo.IsEditable && row.Default == "" && defaultCombo.SelectedItem is null, "配方 Bool 默认值保持未配置，不自动补 false");
        defaultCombo.SelectedItem = "false";
        Check(row.Default == "false", "配方 Bool 默认值下拉写入 false");
        row.ClearDefaultCommand.Execute(null);
        Check(row.Default == "", "配方布尔默认值可以清除");
        Check(File.ReadAllBytes(source).SequenceEqual(before), "所有验证均未改动用户原始 machine.xml 文件");
        File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
