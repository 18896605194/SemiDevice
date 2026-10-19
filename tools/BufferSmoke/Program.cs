using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using BufferControl = xyz.Client.Presentation.Controls.Buffer;
using Path = System.IO.Path;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
            {
                BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark,
                PrimaryColor = MaterialDesignColors.PrimaryColor.Blue,
                SecondaryColor = MaterialDesignColors.SecondaryColor.Lime,
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml") });
            foreach (string name in new[] { "Styles/Color", "Styles/DarkColors", "Styles/FontSize", "Styles/TextBlockStyles",
                         "Styles/BorderStyles", "Styles/ButtonStyles", "Localization/Strings.zh-CN" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/xyz.Client.Presentation;component/{name}.xaml", UriKind.Relative) });
            }

            var scene = (Grid)Application.LoadComponent(new Uri("/BufferSmoke;component/Scene.xaml", UriKind.Relative));
            var model = new BufferPreviewModel();
            scene.DataContext = model;
            var control = (BufferControl)scene.FindName("Rack");
            using var host = new HwndSource(new HwndSourceParameters("Buffer smoke")
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                PositionX = -32000, PositionY = -32000, Width = 460, Height = 420,
            });
            host.RootVisual = scene;
            Pump();
            var innerView = (BufferView)control.FindName("Scene");
            var viewport = (Viewport3D)innerView.FindName("Viewport");
            var camera = (OrthographicCamera)innerView.FindName("Camera");
            var rack = viewport.Children.OfType<HardwareVisual3D>().Single();
            List<DiskVisual3D> Disks() => rack.Children.OfType<DiskVisual3D>().ToList();
            Check(ReferenceEquals(control.DataContext, model) && control.SlotCount == 10 && control.WaferCount == 0,
                "single XAML control inherits the page DataContext and starts with an empty ten-slot rack");
            Check(control.FindName("TopViewButton") is null && !Descendants(control).OfType<ComboBox>().Any()
                && typeof(BufferControl).GetProperty("SelectedSlot") is null, "no top view, drop-down or selection API");

            model.Init();
            Pump();
            Check(control.WaferCount == 10 && Disks().Count == 10, "bound collection fills all ten physical slots");
            Check(Disks().All(disk => !disk.IsSelected), "wafers are display-only");
            Check(ReferenceEquals(Disks()[0].Data, model.Wafers[0]) && Near(Height(Disks()[1]) - Height(Disks()[0]), 0.17),
                "physical slot order and compact spacing");
            var structural = ((Model3DGroup)rack.Content).Children.OfType<GeometryModel3D>().Where(part => part.Material is not EmissiveMaterial).ToList();
            Check(structural.All(part => structural.Any(other => Near(part.Bounds.X, -other.Bounds.X - other.Bounds.SizeX)
                    && Near(part.Bounds.Y, other.Bounds.Y) && Near(part.Bounds.Z, -other.Bounds.Z - other.Bounds.SizeZ)
                    && Near(part.Bounds.SizeX, other.Bounds.SizeX) && Near(part.Bounds.SizeY, other.Bounds.SizeY)
                    && Near(part.Bounds.SizeZ, other.Bounds.SizeZ))), "rack structure is symmetric front/back");

            var firstDisk = Disks()[0];
            model.Wafers[0].State = "Completed";
            Pump();
            var face = (Wafer)firstDisk.Children.OfType<Viewport2DVisual3D>().Single().Visual;
            var ellipse = (Ellipse)face.FindName("diskEllipse");
            Check(ReferenceEquals(firstDisk, Disks()[0]) && ellipse.Fill is SolidColorBrush fill && fill.Color == Colors.Green,
                "in-place state update uses the existing wafer status color without rebuilding disks");

            model.Wafers[0].Slot = 12;
            Pump();
            Check(control.WaferCount == 9 && Disks().Count == 9, "moving a wafer out of the visible range removes it");
            model.Wafers[0].Slot = 1;
            var wafer = model.Wafers[2];
            model.Wafers.Remove(wafer);
            Pump();
            Check(control.WaferCount == 9, "collection removal refreshes occupancy");
            model.Wafers.Insert(2, wafer);
            model.Wafers.Add(new WaferModel { Slot = 3, State = "Error" });
            model.Wafers.Add(new WaferModel { Slot = 30, State = "Error" });
            Pump();
            Check(control.WaferCount == 10 && ReferenceEquals(Disks()[2].Data, wafer), "duplicate and out-of-range slots do not inflate count");

            model.FirstSlot = 6;
            model.SlotCount = 5;
            Pump();
            Check(control.WaferCount == 5 && ((WaferModel)Disks()[0].Data!).Slot == 6 && Near(Height(Disks()[0]), 0.3515),
                "FirstSlot displays a physical slot segment from its bottom level");
            model.FirstSlot = 1;
            model.SlotCount = 10;
            var previous = model.Wafers;
            model.Wafers = [new WaferModel { Slot = 4, State = "Completed", LoadPort = "LoadPort1", LpSlot = "04" }];
            Pump();
            Check(control.WaferCount == 1 && Disks().Count == 1, "replacing the bound source refreshes the rack");
            previous.Clear();
            Check(control.WaferCount == 1, "old source is detached");
            model.Wafers.Clear();
            Check(control.WaferCount == 0 && Disks().Count == 0, "collection reset empties the rack");
            var independent = new BufferControl();
            Check(independent.Wafers is null && independent.WaferCount == 0, "control instances do not share data");
            var panel = new StackPanel();
            panel.Children.Add(independent);
            panel.Measure(new Size(300, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 300, panel.DesiredSize.Height));
            Check(((Viewport3D)((BufferView)independent.FindName("Scene")).FindName("Viewport")).ActualHeight > 150,
                "a control without explicit Height remains visible inside a StackPanel");

            model.Init();
            Pump();
            var position = camera.Position;
            Click(control, "BackViewButton");
            Check(camera.Position.Z < 0, "back view rotates to the other open face");
            Click(control, "DefaultViewButton");
            Check((camera.Position - position).Length < 1e-6, "default view resets the camera");
            double width = camera.Width;
            viewport.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            Check(camera.Width < width, "wheel zoom changes camera scale");
            Click(control, "DefaultViewButton");
            Check(BindingOperations.IsDataBound(control, BufferControl.WafersProperty)
                && BindingOperations.IsDataBound(control, BufferControl.SlotCountProperty), "interaction preserves external bindings");

            if (args.Length > 0)
            {
                Save(scene, args[0]);
                Click(control, "BackViewButton");
                Pump();
                Save(scene, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "buffer-control-back.png"));
                Click(control, "DefaultViewButton");
            }

            model.SlotCount = 0;
            Pump();
            Check(control.WaferCount == 0 && Disks().Count == 0 && ((Model3DGroup)rack.Content).Children.Count == 0,
                "zero slots clears the scene safely");
            model.SlotCount = 25;
            scene.Width = 260;
            Pump();
            Check(double.IsFinite(camera.Width) && camera.Width > 0, "narrow layout and different slot counts refit the camera");
            // 独立视图直接使用页面 DataContext，不带模块外壳；与卡片共享同一套三维实现。
            var bareScene = (Grid)Application.LoadComponent(new Uri("/BufferSmoke;component/Standalone.xaml", UriKind.Relative));
            var bare = (BufferView)bareScene.FindName("RackView");
            var bareModel = new BufferPreviewModel();
            bareScene.DataContext = bareModel;
            bareModel.Init();
            host.RootVisual = bareScene;
            Pump();
            var bareViewport = (Viewport3D)bare.FindName("Viewport");
            var bareRack = bareViewport.Children.OfType<BufferVisual3D>().Single();
            Check(bare.WaferCount == 10 && bareRack.WaferCount == 10 && ReferenceEquals(bare.DataContext, bareModel),
                "standalone view binds through inherited page DataContext");
            Check(!Descendants(bare).OfType<Button>().Any() && ((Grid)bare.Content).Children.Count == 1,
                "standalone view contains only the rack, with no module title, status or toolbar");
            bareModel.Wafers.RemoveAt(0);
            Pump();
            Check(bare.WaferCount == 9 && control.WaferCount == 10, "standalone instance refreshes independently of module card");
            bareModel.Init();
            Pump();
            if (args.Length > 0)
            {
                Save(bareScene, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "buffer-view.png"));
            }

            // 直接挂到共享视口：显式绑定数据源，外部 Transform 与缓存架数据更新相互独立。
            var sharedScene = (Grid)Application.LoadComponent(new Uri("/BufferSmoke;component/SharedScene.xaml", UriKind.Relative));
            sharedScene.DataContext = bareModel;
            host.RootVisual = sharedScene;
            Pump();
            var sharedRack = (BufferVisual3D)sharedScene.FindName("SharedRack");
            Check(sharedRack.WaferCount == 10 && sharedRack.Children.OfType<DiskVisual3D>().Count() == 10,
                "public BufferVisual3D is usable directly in XAML with explicit source bindings");
            var transform = sharedRack.Transform;
            bareModel.Wafers[0].Slot = 30;
            Check(sharedRack.WaferCount == 9 && bare.WaferCount == 9, "both public entry points observe wafer slot movement");
            bareModel.SlotCount = 5;
            Pump();
            Check(sharedRack.WaferCount == 4 && ReferenceEquals(sharedRack.Transform, transform),
                "geometry updates preserve the host's external transform");
            var oldWafers = bareModel.Wafers;
            bareModel.Wafers = [new WaferModel { Slot = 2, State = "Completed" }];
            Pump();
            oldWafers.Clear();
            Check(sharedRack.WaferCount == 1 && bare.WaferCount == 1, "replacement detaches old sources at the shared geometry layer");
            bareModel.SlotCount = 0;
            Pump();
            Check(sharedRack.WaferCount == 0 && bare.WaferCount == 0, "all independent entry points handle zero slots");
            host.RootVisual = null;
            app.Shutdown();
            Console.WriteLine($"PASS: {_checks} Buffer checks (XAML binding, collections, slot movement, colors, symmetry, view controls, zoom, empty/range states).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static double Height(DiskVisual3D disk) => ((TranslateTransform3D)disk.Transform).OffsetY;
    private static bool Near(double left, double right) => Math.Abs(left - right) < 1e-6;
    private static void Click(BufferControl control, string name) => ((Button)control.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Save(FrameworkElement scene, string output)
    {
        var bitmap = new RenderTargetBitmap((int)scene.ActualWidth, (int)scene.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        // 将控件自身坐标归零；固定尺寸的示例在较大宿主里居中时也导出完整内容。
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(scene), null, new Rect(0, 0, scene.ActualWidth, scene.ActualHeight));
        }

        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var file = File.Create(output);
        encoder.Save(file);
        Console.WriteLine($"Preview: {output}");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        _checks++;
    }
}

internal sealed class BufferPreviewModel : BaseViewModel
{
    #region Column
    private ObservableCollection<WaferModel> _wafers = [];
    public ObservableCollection<WaferModel> Wafers { get => _wafers; set => SetProperty(ref _wafers, value); }
    private int _firstSlot = 1;
    public int FirstSlot { get => _firstSlot; set => SetProperty(ref _firstSlot, value); }
    private int _slotCount = 10;
    public int SlotCount { get => _slotCount; set => SetProperty(ref _slotCount, value); }
    #endregion

    public override void Init()
    {
        Wafers = new ObservableCollection<WaferModel>(Enumerable.Range(1, 10).Select(slot => new WaferModel
        { Slot = slot, LoadPort = "LoadPort1", LpSlot = slot.ToString("00"), State = slot <= 5 ? "IdleHasjob" : "Completed" }));
    }
}
