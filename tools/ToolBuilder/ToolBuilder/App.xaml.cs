using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
using ChamberLayoutEditor;
using Microsoft.Extensions.DependencyInjection;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Tools;

namespace ToolBuilder;

public partial class App : Application
{
    private void GridComboLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        combo.ApplyTemplate();
        if (combo.Template.FindName("PART_EditableTextBox", combo) is TextBox text) text.TextAlignment = TextAlignment.Center;
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, failure) =>
        {
            string folder = e.Args.Length >= 3 && e.Args[0].EndsWith("-test", StringComparison.Ordinal) ? e.Args[2] : AppContext.BaseDirectory;
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "error.txt"), failure.Exception.ToString());
            failure.Handled = true; Shutdown(1);
        };
        IocHelper.ServiceProvider = new ServiceCollection().AddChamberLayoutEditor().BuildServiceProvider();
        L10n.AddPack("ToolBuilder.ChamberLayout");
        var window = new MainWindow(e.Args.FirstOrDefault() is not ("--preview" or "--layout-test" or "--config-test" or "--chamber-test"));
        foreach (var model in IocHelper.ServiceProvider.GetServices<BaseViewModel>()) model.Init();
        MainWindow = window;
        if (e.Args.FirstOrDefault() is "--layout-test" or "--config-test" or "--chamber-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                if (e.Args[0] == "--chamber-test")
                {
                    await ChamberLayoutSmoke.Run(window, e.Args[1], e.Args[2], e.Args.Skip(3).ToArray());
                }
                else if (e.Args[0] == "--config-test")
                {
                    await ConfigEditorSmoke.Run(window, e.Args[1], e.Args[2]);
                }
                else
                {
                    await ToolBuilderSmoke.Run(window, e.Args[1], e.Args[2], e.Args.Skip(3).ToArray());
                }
                Shutdown(0);
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(e.Args[2]);
                File.WriteAllText(Path.Combine(e.Args[2], "error.txt"), exception.ToString());
                Shutdown(1);
            }
            return;
        }
        if (e.Args.FirstOrDefault() == "--preview")
        {
            // 可复现的界面验证：离屏布局和截图，不修改输入 machine.xml。
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var errors = new List<string>();
            var listener = new BindingTraceListener(errors);
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            try
            {
                if (e.Args.Length < 4) throw new ArgumentException("--preview <machine.xml> <输出 png> <元件目录...>");
                window.ViewModel.OpenFile(e.Args[1]);
                window.ViewModel.Sources.Clear();
                foreach (var source in e.Args.Skip(3)) window.ViewModel.Sources.Add(source);
                await window.ViewModel.LoadCatalogAsync();
                window.ViewModel.SelectNode(window.ViewModel.Roots.SelectMany(r => r.Walk()).FirstOrDefault(n => n.Name == "Door") ?? window.ViewModel.Roots.FirstOrDefault());
                var content = (FrameworkElement)window.Content;
                content.Width = 1600; content.Height = 1000;
                content.Measure(new Size(1600, 1000)); content.Arrange(new Rect(0, 0, 1600, 1000)); content.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap(1600, 1000, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(e.Args[2])) encoder.Save(stream);
                if (window.ViewModel.Elements.Count == 0) errors.Add("没有识别到元件。");
                if (!new[] { "IsRun", "DoOpenIndex", "DoCloseIndex" }.All(name => window.ViewModel.Fields.Any(field => field.Name == name)))
                {
                    errors.Add("腔门继承配置字段未展示。");
                }
                window.ViewModel.SelectNode(window.ViewModel.Roots.SelectMany(r => r.Walk()).First(n => n.Path == "Safety.Emo"));
                var triggerField = window.ViewModel.Fields.Single(f => f.Name == "TriggerValue");
                if (triggerField.Spec.Kind != "System.Boolean" || !triggerField.Choices.SequenceEqual(new[] { "true", "false" })
                    || triggerField.Value != "false" || triggerField.DefaultValue != "true") errors.Add("Emo 触发电平应显示为 false，默认 true，提供布尔选项。");
                var triggerElement = new System.Xml.Linq.XElement(window.ViewModel.Selected!.Element);
                var triggerCopy = new FieldModel(triggerElement, triggerField.Spec) { Value = "true" };
                if (ToolBuilder.Core.ConfigDocument.Value(triggerElement, "TriggerValue") != "true") errors.Add("修改 DI 触发电平应写入布尔值。");
                ToolBuilder.Core.ConfigDocument.SetValue(triggerElement, "TriggerValue", "Low");
                if (triggerCopy.Error is null) errors.Add("DI 触发电平只认 true/false，旧的 High/Low 写法应报错。");
                content.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var diBitmap = new RenderTargetBitmap(1600, 1000, 96, 96, PixelFormats.Pbgra32); diBitmap.Render(content);
                var diEncoder = new PngBitmapEncoder(); diEncoder.Frames.Add(BitmapFrame.Create(diBitmap));
                using (var stream = File.Create(e.Args[2] + ".di.png")) diEncoder.Save(stream);
                var sourcesWindow = new SourcesWindow(window.ViewModel);
                var sourcesContent = (FrameworkElement)sourcesWindow.Content;
                sourcesContent.Margin = new Thickness(0);
                sourcesContent.Width = 770; sourcesContent.Height = 410;
                sourcesContent.Measure(new Size(770, 410)); sourcesContent.Arrange(new Rect(0, 0, 770, 410)); sourcesContent.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var sourcesBitmap = new RenderTargetBitmap(770, 410, 96, 96, PixelFormats.Pbgra32); sourcesBitmap.Render(sourcesContent);
                var sourcesEncoder = new PngBitmapEncoder(); sourcesEncoder.Frames.Add(BitmapFrame.Create(sourcesBitmap));
                using (var stream = File.Create(e.Args[2] + ".sources.png")) sourcesEncoder.Save(stream);
                var tabs = (TabControl)window.FindName("EditorTabs");
                if (tabs.Items.Cast<TabItem>().Any(tab => tab.Header?.ToString() == "子元件")) errors.Add("右侧不应重复显示子元件页。");
                var robotNode = window.ViewModel.Roots.SelectMany(r => r.Walk()).First(n => n.Name == "Robot1");
                window.ViewModel.SelectNode(robotNode);
                content.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (window.ViewModel.EditorPage != "Parameters" || !((TabItem)window.FindName("ParametersTab")).IsSelected
                    || !window.ViewModel.Fields.Any(f => f.Name == "IsEnable")) errors.Add("Robot1 应直接显示自身参数。");
                if (!robotNode.IsExpanded || robotNode.Children.All(c => c.Name != "Tcp")) errors.Add("Robot1 应在左侧展开现有子节点。");
                var robotBitmap = new RenderTargetBitmap(1600, 1000, 96, 96, PixelFormats.Pbgra32); robotBitmap.Render(content);
                var robotEncoder = new PngBitmapEncoder(); robotEncoder.Frames.Add(BitmapFrame.Create(robotBitmap));
                using (var stream = File.Create(e.Args[2] + ".robot.png")) robotEncoder.Save(stream);
                window.ViewModel.SelectNode(robotNode.Children.First(n => n.Name == "Tcp"));
                content.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (window.ViewModel.EditorPage != "Parameters" || window.ViewModel.Fields.Count == 0) errors.Add("选择 Tcp（机械手的网口通道）应显示其自身参数。");
                // 机械手示教点（Size12 / Size8 下的.TeachPointElement）按普通元件显示：参数全从元件的 [Machine] 来，没有"原有参数"
                var teachPoint = robotNode.Walk().FirstOrDefault(n => n.Type.EndsWith(".TeachPointElement", StringComparison.Ordinal));
                if (teachPoint is null) errors.Add("Robot1 下应有示教点（Size12 / Size8 里的.TeachPointElement）。");
                else
                {
                    window.ViewModel.SelectNode(teachPoint);
                    content.UpdateLayout();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    if (window.ViewModel.EditorPage != "Parameters"
                        || !new[] { "Number", "Y", "Direction", "PickArms", "PlaceArms" }.All(name => window.ViewModel.Fields.Any(f => f.Name == name))
                        || window.ViewModel.Fields.Any(f => f.Spec.Group == "原有参数")) errors.Add("选择示教点应按元件参数显示站号、方向、伸出距离、取放手指。");
                    var pointBitmap = new RenderTargetBitmap(1600, 1000, 96, 96, PixelFormats.Pbgra32);
                    pointBitmap.Render(content);
                    var pointEncoder = new PngBitmapEncoder(); pointEncoder.Frames.Add(BitmapFrame.Create(pointBitmap));
                    using (var stream = File.Create(e.Args[2] + ".teach-point.png")) pointEncoder.Save(stream);
                }
                // 配方字段页：选工艺配方下的 Fields 进字段表（一行一个字段，时间那一行锁着），真 machine.xml 的字段表没有错误；出字段页和选项窗口的图
                var recipeFieldsTab = (TabItem)window.FindName("RecipeFieldsTab");
                var fieldsNode = window.ViewModel.Roots.SelectMany(r => r.Walk()).FirstOrDefault(n => ToolBuilder.Core.RecipeFields.IsFieldsNode(n.Element));
                if (fieldsNode is null) errors.Add("machine.xml 里应有工艺配方的字段表（ProcessRecipe.Fields）。");
                else
                {
                    window.ViewModel.SelectNode(fieldsNode);
                    content.UpdateLayout();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    if (recipeFieldsTab.Visibility != Visibility.Visible || !recipeFieldsTab.IsSelected
                        || window.ViewModel.RecipeFieldRows.Count != fieldsNode.Children.Count) errors.Add("选择 Fields 应进入配方字段页，一行一个字段。");
                    var secondsRow = window.ViewModel.RecipeFieldRows.FirstOrDefault(r => r.Key == ToolBuilder.Core.RecipeFields.SecondsKey);
                    if (secondsRow is null || !secondsRow.IsLocked || window.ViewModel.DeleteRecipeFieldCommand.CanExecute(null) && window.ViewModel.SelectedRecipeField == secondsRow)
                        errors.Add("时间（Seconds）那一行应锁着、删不了。");
                    if (window.ViewModel.Issues.Any(i => i.Severity == "错误" && i.Path.StartsWith(fieldsNode.Path, StringComparison.Ordinal)))
                        errors.Add("真 machine.xml 的工艺配方字段表不应有校验错误。");
                    var chemicalRow = window.ViewModel.RecipeFieldRows.FirstOrDefault(r => r.Key == "Chemical");
                    window.ViewModel.SelectedRecipeField = chemicalRow;
                    var fieldsGrid = (DataGrid)window.FindName("RecipeFieldsGrid");
                    fieldsGrid.SelectedItem = chemicalRow;
                    content.UpdateLayout();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var fieldsBitmap = new RenderTargetBitmap(1600, 1000, 96, 96, PixelFormats.Pbgra32);
                    fieldsBitmap.Render(content);
                    var fieldsEncoder = new PngBitmapEncoder(); fieldsEncoder.Frames.Add(BitmapFrame.Create(fieldsBitmap));
                    using (var stream = File.Create(e.Args[2] + ".recipe-fields.png")) fieldsEncoder.Save(stream);
                    if (window.ViewModel.RecipeOptionTypes.Count == 0 || fieldsGrid.Columns.Count(c => (c.Header as string)?.StartsWith("选项") == true) != window.ViewModel.RecipeOptionTypes.Count)
                        errors.Add("配方字段页应按腔体的 Type 一种一列选项。");
                    if (chemicalRow is null) errors.Add("字段表里应有药液（Chemical）。");
                    else
                    {
                        var chemicalCell = chemicalRow.OptionCells[0];
                        var sourceModel = new OptionsDialogViewModel(chemicalRow.Key, chemicalRow.Text, chemicalCell.StationType, chemicalCell.Text);
                        if (sourceModel.Items.Count == 0 || sourceModel.Result != chemicalCell.Text)
                            errors.Add($"选项窗口应把 {chemicalCell.Text} 拆成一行一个，原样连回去。");
                        var sourceDialog = new OptionsDialog(sourceModel);
                        var sourceContent = (FrameworkElement)sourceDialog.Content;
                        // 跟窗口一样大（外边距算在里面；标题栏不算）
                        var sourceWidth = sourceDialog.Width;
                        var sourceHeight = sourceDialog.Height - SystemParameters.WindowCaptionHeight;
                        sourceContent.Measure(new Size(sourceWidth, sourceHeight));
                        sourceContent.Arrange(new Rect(0, 0, sourceWidth, sourceHeight)); sourceContent.UpdateLayout();
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        var sourceBitmap = new RenderTargetBitmap((int)sourceWidth, (int)Math.Ceiling(sourceHeight), 96, 96, PixelFormats.Pbgra32);
                        var sourceVisual = new DrawingVisual();
                        using (var context = sourceVisual.RenderOpen())
                        {
                            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, sourceWidth, sourceHeight));
                            context.DrawRectangle(new VisualBrush(sourceContent) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, sourceWidth, sourceHeight) },
                                null, new Rect(0, 0, sourceWidth, sourceHeight));
                        }
                        sourceBitmap.Render(sourceVisual);
                        var sourceEncoder = new PngBitmapEncoder(); sourceEncoder.Frames.Add(BitmapFrame.Create(sourceBitmap));
                        using (var stream = File.Create(e.Args[2] + ".recipe-options.png")) sourceEncoder.Save(stream);
                    }
                }
                // 配方头页：选工艺配方下的 Head 进配方头表（固定的几项锁着，Type 不写选项），真 machine.xml 的配方头表没有错误；出图
                var recipeHeadTab = (TabItem)window.FindName("RecipeHeadTab");
                var headNode = window.ViewModel.Roots.SelectMany(r => r.Walk()).FirstOrDefault(n => ToolBuilder.Core.RecipeFields.IsHeadNode(n.Element));
                if (headNode is null) errors.Add("machine.xml 里应有工艺配方的配方头表（ProcessRecipe.Head）。");
                else
                {
                    window.ViewModel.SelectNode(headNode);
                    content.UpdateLayout();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    if (recipeHeadTab.Visibility != Visibility.Visible || !recipeHeadTab.IsSelected || window.ViewModel.RecipeFieldRows.Count != headNode.Children.Count)
                        errors.Add("选择 Head 应进入配方头页，一行一项。");
                    var typeRow = window.ViewModel.RecipeFieldRows.FirstOrDefault(r => r.Key == ToolBuilder.Core.RecipeFields.TypeKey);
                    if (typeRow is null || !typeRow.IsLocked || typeRow.HasOptions || !window.ViewModel.RecipeFieldRows.All(r => r.IsLocked))
                        errors.Add("配方头固定的几项应锁着，Type 不写选项。");
                    if (window.ViewModel.Issues.Any(i => i.Severity == "错误" && i.Path.StartsWith(headNode.Path, StringComparison.Ordinal)))
                        errors.Add("真 machine.xml 的配方头表不应有校验错误。");
                    var headBitmap = new RenderTargetBitmap(1600, 1000, 96, 96, PixelFormats.Pbgra32);
                    headBitmap.Render(content);
                    var headEncoder = new PngBitmapEncoder(); headEncoder.Frames.Add(BitmapFrame.Create(headBitmap));
                    using (var stream = File.Create(e.Args[2] + ".recipe-head.png")) headEncoder.Save(stream);
                }
                window.ViewModel.SelectNode(window.ViewModel.Roots.SelectMany(root => root.Walk()).First(n => n.Name == "Hsms"));
                content.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (!window.ViewModel.Fields.Any(f => f.HasChoices)) errors.Add("布尔/枚举编辑器未生成。");
                if (window.ViewModel.EditorPage != "Parameters") errors.Add("切换到其他元件后应显示自身参数。");
                foreach (var page in new[] { "Validation", "Xml" })
                {
                    window.ViewModel.EditorPage = page; content.UpdateLayout();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }
                if (window.ViewModel.Document?.IsDirty == true) errors.Add("界面初始化不应修改配置。");
                var before = File.ReadAllBytes(e.Args[1]);
                var vm = window.ViewModel;
                var initialNodes = vm.Document!.Settings.Count();
                var door = vm.Roots.SelectMany(r => r.Walk()).First(n => n.Name == "Door");
                vm.SelectNode(door);
                vm.DuplicateCommand.Execute(null);
                if (vm.Document.Settings.Count() != initialNodes + door.Walk().Count() || vm.Selected?.Name != "DoorCopy")
                {
                    errors.Add("复制节点及其子元件未同步 XML 和配置树。");
                }
                var copied = vm.Selected!;
                vm.UpCommand.Execute(null);
                if (copied.Parent?.Children[0] != copied || copied.Parent.Element.Elements("Setting").First() != copied.Element) errors.Add("上移节点未同步 XML。");
                vm.DownCommand.Execute(null);
                vm.Fields.First(f => f.Name == "DoOpenIndex").Value = "321";
                if (ToolBuilder.Core.ConfigDocument.Value(copied.Element, "DoOpenIndex") != "321") errors.Add("字段修改未写入 XML。");
                var robot = vm.Roots.SelectMany(r => r.Walk()).First(n => n.Name == "Robot1");
                var picker = new ElementPicker(vm.Elements, robot.Path);
                if (!picker.ViewModel.Categories.SequenceEqual(new[] { "元件", "分组" })
                    || vm.Elements.Any(c => c.Category != "元件")) errors.Add("添加窗口应只有元件和分组两类。");
                picker.ViewModel.Search = "气缸";
                if (!picker.ViewModel.FilteredElements.Cast<ToolBuilder.Core.ElementSpec>().Any(c => c.ShortName == "CylinderElement")) errors.Add("元件库不能按中文用途搜索。");
                if (picker.ViewModel.ConfirmCommand.CanExecute(null)) errors.Add("元件未选类型时不应允许添加。");
                picker.ViewModel.SelectedElement = vm.Elements.First(c => c.ShortName == "CylinderElement");
                if (!picker.ViewModel.ConfirmCommand.CanExecute(null)) errors.Add("元件选择后无法确认添加。");
                if (picker.FindName("NodeNameBox") is not null) errors.Add("添加窗口不应要求填写名称。");
                var pickerContent = (FrameworkElement)picker.Content;
                pickerContent.Margin = new Thickness(0);
                pickerContent.Width = 1035; pickerContent.Height = 680;
                pickerContent.Measure(new Size(1035, 680)); pickerContent.Arrange(new Rect(0, 0, 1035, 680)); pickerContent.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var pickerBitmap = new RenderTargetBitmap(1035, 680, 96, 96, PixelFormats.Pbgra32); pickerBitmap.Render(pickerContent);
                var pickerEncoder = new PngBitmapEncoder(); pickerEncoder.Frames.Add(BitmapFrame.Create(pickerBitmap));
                using (var stream = File.Create(e.Args[2] + ".add-component.png")) pickerEncoder.Save(stream);
                var added = vm.CreateChild(robot, picker.ViewModel.SelectedElement);
                if (added.Parent != robot || added.Type != "xyz.Elements.Elements.CylinderElement" || robot.Children.All(c => c != added)) errors.Add("Robot1 下创建元件没有完整记录类型和父节点。");
                if (vm.Fields.Count != picker.ViewModel.SelectedElement!.Properties.Count || vm.Fields.Any(f => ToolBuilder.Core.ConfigDocument.Value(added.Element, f.Name) != f.DefaultValue))
                {
                    errors.Add("新元件未生成完整默认配置。");
                }
                if (vm.Selected != added || vm.EditorPage != "Parameters") errors.Add("添加元件后应直接选中自身参数页。");
                var repeatedElement = vm.CreateChild(robot, picker.ViewModel.SelectedElement);
                if (repeatedElement.Name.Equals(added.Name, StringComparison.OrdinalIgnoreCase)) errors.Add("重复添加元件应自动生成不重名的名称。");
                vm.SelectNode(added); content.UpdateLayout();
                var nameEditor = (TextBox)window.FindName("NodeNameEditor");
                nameEditor.SetCurrentValue(TextBox.TextProperty, "CylinderTest");
                nameEditor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (added.Name != "CylinderTest" || added.Path != "Robot.Robot1.CylinderTest") errors.Add("主界面 Name 编辑未同步元件和配置树路径。");
                var rejected = false;
                try { vm.CreateChild(robot, "CylinderTest", picker.ViewModel.SelectedElement); } catch (ArgumentException) { rejected = true; }
                if (!rejected) errors.Add("底层创建未拦截重名。");
                picker.ViewModel.Category = "分组";
                if (!picker.ViewModel.IsGroup || picker.ViewModel.SelectedElement is not null
                    || picker.ViewModel.SelectedProperties.Any() || !picker.ViewModel.ConfirmCommand.CanExecute(null)) errors.Add("分组应无需类型和名称输入即可创建。");
                pickerContent.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var groupBitmap = new RenderTargetBitmap(1035, 680, 96, 96, PixelFormats.Pbgra32); groupBitmap.Render(pickerContent);
                var groupEncoder = new PngBitmapEncoder(); groupEncoder.Frames.Add(BitmapFrame.Create(groupBitmap));
                using (var stream = File.Create(e.Args[2] + ".add-group.png")) groupEncoder.Save(stream);
                var addedGroup = vm.CreateChild(robot, picker.ViewModel.IsGroup ? null : picker.ViewModel.SelectedElement);
                if (addedGroup.Category != "分组" || addedGroup.Element.Attribute("Type") is not null
                    || addedGroup.Element.Elements("Value").Any()) errors.Add("分组不应写入 Type 或元件默认参数。");
                if (vm.Selected != addedGroup) errors.Add("添加分组后应直接选中新节点。");
                var repeatedGroup = vm.CreateChild(robot, null);
                if (repeatedGroup.Name.Equals(addedGroup.Name, StringComparison.OrdinalIgnoreCase)) errors.Add("重复添加分组应自动生成不重名的名称。");
                vm.SelectNode(addedGroup); content.UpdateLayout();
                nameEditor.SetCurrentValue(TextBox.TextProperty, "GroupTest");
                nameEditor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (addedGroup.Name != "GroupTest" || addedGroup.Path != "Robot.Robot1.GroupTest") errors.Add("主界面 Name 编辑未同步分组和配置树路径。");
                picker.ViewModel.Category = "元件";
                if (picker.ViewModel.ConfirmCommand.CanExecute(null) || !picker.ViewModel.IsElement
                    || picker.ViewModel.FilteredElements.IsEmpty) errors.Add("切回元件时应重新选择 DLL 类型。");
                var noDllPicker = new ElementPicker([], robot.Path, "分组");
                if (!noDllPicker.ViewModel.ConfirmCommand.CanExecute(null)) errors.Add("未加载 DLL 时也应允许添加分组。");
                var temp = Path.Combine(Path.GetTempPath(), "ToolBuilderUi-" + Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    vm.Document.Save(temp);
                    var readBack = ToolBuilder.Core.ConfigDocument.Open(temp);
                    if (!readBack.Settings.Any(n => ToolBuilder.Core.ConfigDocument.Attribute(n, "Name") == "DoorCopy" && ToolBuilder.Core.ConfigDocument.Value(n, "DoOpenIndex") == "321")) errors.Add("界面修改保存后无法重新读取。");
                    if (!readBack.Settings.Any(n => ToolBuilder.Core.ConfigDocument.PathOf(n) == "Robot.Robot1.CylinderTest" && ToolBuilder.Core.ConfigDocument.Attribute(n, "Type") == added.Type)) errors.Add("Robot1 新元件保存后无法重读。");
                    if (!readBack.Settings.Any(n => ToolBuilder.Core.ConfigDocument.PathOf(n) == "Robot.Robot1.GroupTest"
                        && n.Attribute("Type") is null && !n.Elements("Value").Any())) errors.Add("Robot1 新分组保存后无法重读。");
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                if (!File.ReadAllBytes(e.Args[1]).SequenceEqual(before)) errors.Add("界面验证修改了输入 machine.xml。");
                File.WriteAllText(e.Args[2] + ".log", $"元件：{vm.Elements.Count}\n上下文页面切换和布尔/枚举控件已验证\n添加窗口：仅元件和分组两类；无需填写名称、自动避免重名、主界面 Name 修改、默认参数及保存重读已验证\n复制、上移/下移、参数修改和另存为往返已验证\n" + string.Join("\n", errors));
                Shutdown(errors.Count == 0 ? 0 : 1);
            }
            catch (Exception exception)
            {
                if (e.Args.Length > 2) File.WriteAllText(e.Args[2] + ".log", exception.ToString());
                Shutdown(1);
            }
            return;
        }
        window.Show();
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
        {
            try
            {
                window.ViewModel.OpenFile(e.Args[0]); await window.ViewModel.LoadCatalogAsync();
                if (e.Args.Length >= 3 && e.Args[1] == "--select")
                {
                    window.ViewModel.SelectNode(window.ViewModel.Roots.SelectMany(r => r.Walk()).FirstOrDefault(n => n.Name == e.Args[2] || n.Path == e.Args[2]));
                }
            }
            catch (Exception exception) { MessageBox.Show(exception.Message, "打开 machine.xml 失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    private sealed class BindingTraceListener(List<string> messages) : TraceListener
    {
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
