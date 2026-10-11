using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using xyz.Client.Manual.Models;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;

// LoadPort 三维图冒烟：LoadPort（LoadPortVisual3D）+ 托板上的花篮（CarrierVisual3D，片用 DiskVisual3D）组合起来的 LoadPortScene。
// 槽数照 sc（花篮高度、槽号、片数、取景跟着变）、片的数据就地换、点片选中、每个动作 0.5 s 且有先后（先推进去再开门 / 先关门再退出）、
// 只亮在动的那块、门在半路停中间一直亮、中途反向不跳、藏起来直接落位、面板灯、视角工具栏、片数和空槽的字，
// 以及手动页的显示模型按状态和门反馈推出对接 / 门开 / 门在半路。不连后端。
internal static class Program
{
    private const double DockTravel = 0.72;
    private const double DoorRetract = 0.65;
    private const double DoorDrop = 3.5;
    private const double DoorRetractShare = 0.22;

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
            foreach (string name in new[] { "Styles/Color", "Styles/DarkColors", "Styles/FontSize", "Styles/TextBlockStyles", "Styles/BorderStyles",
                         "Styles/ButtonStyles", "Localization/Strings.zh-CN" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/xyz.Client.Presentation;component/{name}.xaml", UriKind.Relative) });
            }

            var scene = new LoadPortScene { Width = 440, Height = 560 };
            using var host = new HwndSource(new HwndSourceParameters("LoadPort scene smoke")
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                PositionX = -32000, PositionY = -32000, Width = 440, Height = 560
            });
            host.RootVisual = scene;
            Pump(60);
            var port = (LoadPortVisual3D)scene.FindName("Port");
            var camera = (OrthographicCamera)scene.FindName("Camera");
            var viewport = (Viewport3D)scene.FindName("Viewport");
            var door = (HardwareVisual3D)port.Children[0];
            var carriage = (HardwareVisual3D)port.Children[1];
            var doorMove = (TranslateTransform3D)door.Transform;
            var carriageMove = (TranslateTransform3D)carriage.Transform;
            CarrierVisual3D? Carrier() => port.Attachments.OfType<CarrierVisual3D>().SingleOrDefault();
            string Text(string name) => ((TextBlock)scene.FindName(name)).Text;
            bool Shown(string name) => ((FrameworkElement)scene.FindName(name)).Visibility == Visibility.Visible;
            double Dock() => 1 - carriageMove.OffsetZ / DockTravel;
            double Door()
            {
                double retract = -doorMove.OffsetZ / DoorRetract;
                double drop = -doorMove.OffsetY / DoorDrop;
                return drop > 0 ? DoorRetractShare + drop * (1 - DoorRetractShare) : retract * DoorRetractShare;
            }

            Color idleDoor = ColorOf(door);
            Color idleCarriage = ColorOf(carriage);
            bool Lit(HardwareVisual3D part) => ColorOf(part) != (ReferenceEquals(part, door) ? idleDoor : idleCarriage);

            // 1. 没载具、或者有载具但还不知道槽数（sc 的 SlotCount 还没推上来）：托板上不装花篮，片数和框外一行都不写
            Check(Carrier() is null && !Shown("Counter") && ((FrameworkElement)scene.FindName("InfoRow")).Visibility != Visibility.Visible,
                "没载具：不装花篮、不写片数和空槽");
            scene.IsCarrierPresent = true;
            Pump(20);
            Check(Carrier() is null, "槽数还是 0（sc 没推上来）：不装花篮，也不按 25 槽兜底");

            // 2. 槽数照 sc：25 槽 → 花篮装上，槽号 01 / 05 / … / 25 六个；片数、空槽按槽数算
            scene.Wafers = Wafers(25, [6, 12, 20]);
            scene.SlotCount = 25;
            Pump(20);
            var carrier = Carrier();
            Check(carrier is not null && carrier.SlotCount == 25, "有载具、槽数 25：花篮装到托板上");
            Check(Labels(carrier!) == 6, "25 槽标 6 个槽号（1、5、10、15、20、25）");
            Check(Text("WaferCount") == "22" && Text("SlotTotal") == "/ 25 片" && Text("EmptySlotsText") == "空槽 06 / 12 / 20" && Shown("ClickHint"),
                "右上角 22 / 25 片，框外写空槽、没点片时写提示");
            double height25 = Height(carrier!);
            double width25 = camera.Width;
            scene.SlotCount = 13;
            scene.Wafers = Wafers(25, [6, 12, 20]);
            Pump(20);
            Check(carrier!.SlotCount == 13 && Labels(carrier) == 4 && Height(carrier) < height25, "13 槽：花篮矮了，槽号 1、5、10、13");
            Check(Disks(carrier).Count == 11 && Text("WaferCount") == "11" && Text("SlotTotal") == "/ 13 片" && Text("EmptySlotsText") == "空槽 06 / 12",
                "超出槽数的片不画，片数、空槽按 13 槽算");
            scene.SlotCount = 26;
            Pump(20);
            Check(Labels(carrier) == 7 && Height(carrier) > height25, "26 槽：花篮高了，最后一槽 26 也标号");
            scene.SlotCount = 40;
            scene.Wafers = Wafers(40, []);
            Pump(20);
            Check(camera.Width > width25 * 1.05 && Text("EmptySlotsText") == "满载 · 40 片", "40 槽花篮高出背板：取景跟着放大不切掉，满载写 40 片");
            scene.SlotCount = 25;
            scene.Wafers = Wafers(25, [6, 12, 20]);
            Pump(20);
            Check(Math.Abs(camera.Width - width25) < 1e-6, "回到 25 槽取景复原");

            // 3. 片用腔体那个 DiskVisual3D：数据就是晶圆账的片、从下往上按槽号摆；推送来了槽没变只换数据不重建
            var disks = Disks(carrier);
            Check(disks.Count == 22 && disks.Select(disk => ((WaferModel)disk.Data!).Slot).SequenceEqual(Enumerable.Range(1, 25).Where(slot => slot != 6 && slot != 12 && slot != 20)),
                "有片的槽各一片 DiskVisual3D，按槽号从下往上");
            Check(disks.Zip(disks.Skip(1)).All(pair => pair.Second.Transform.Value.OffsetY > pair.First.Transform.Value.OffsetY), "槽号越大越高");
            Check(disks.All(disk => disk.FillColor is SolidColorBrush fill && fill.Color == ((SolidColorBrush)app.FindResource("DarkHardwareDiskFill")).Color),
                "盘面底色跟腔体旋转盘同一个 token");
            var fresh = Wafers(25, [6, 12, 20]);
            fresh[0].State = "Completed";
            scene.Wafers = fresh;
            Pump(20);
            var again = Disks(carrier);
            Check(again.Zip(disks).All(pair => ReferenceEquals(pair.First, pair.Second)) && ReferenceEquals(again[0].Data, fresh[0]),
                "槽没变：还是那些片，只换数据（状态色跟着变）");
            scene.Wafers = Wafers(25, [6, 12]);
            Pump(20);
            Check(Disks(carrier).Count == 23 && Text("EmptySlotsText") == "空槽 06 / 12", "多一片：补上那一槽");

            // 4. 点片：俯视点最上面那片选中（点在盘面上也认得出是哪一槽）；点空的地方不改选中；那一槽没片了取消选中
            Click(scene, "TopViewButton");
            Pump(20);
            var top = Disks(carrier).Last();
            Point center = ((GeneralTransform3DTo2D)top.TransformToAncestor(viewport)).Transform(new Point3D(0, 0.03, 0));
            Select(scene, center);
            Check(carrier.SelectedSlot == 25 && top.IsSelected && Shown("SelectedSlotText") && Text("SelectedSlotText") == "槽位 25" && !Shown("ClickHint"),
                "点最上面那片：选中第 25 槽、那片高亮，框外写槽位 25");
            Select(scene, new Point(5, 5));
            Check(carrier.SelectedSlot == 25, "点空的地方不改选中");
            scene.Wafers = Wafers(25, [6, 12, 25]);
            Pump(20);
            Check(carrier.SelectedSlot == 0 && Shown("ClickHint"), "选中的那一槽片没了：取消选中，回到提示");
            Click(scene, "DefaultViewButton");

            // 5. 装载：先推进去（托板亮）、再开门（门亮），每个动作 0.5 s
            Check(Near(Dock(), 0) && Near(Door(), 0), "开始：退出、门关");
            scene.IsDocked = true;
            scene.IsDoorOpen = true;
            Pump(250);
            Check(Dock() > 0.15 && Dock() < 0.85 && Near(Door(), 0) && Lit(carriage) && !Lit(door), "0.25 s：托板推到一半、门还没动，只亮托板");
            Pump(150);
            Check(Dock() < 1 && Near(Door(), 0), "0.4 s：托板还没到（一个动作 0.5 s）");
            Pump(250);
            Check(Near(Dock(), 1) && Door() > 0 && Door() < 0.6 && Lit(door) && !Lit(carriage), "0.65 s：托板到了、门开始开，只亮门");
            Pump(650);
            Check(Near(Door(), 1) && !port.HasAnimatedProperties && !Lit(door) && !Lit(carriage), "1.3 s：门开到底（往后让开再往下收），动画、高亮都撤了");
            Check(Near(carriageMove.OffsetZ, 0) && Near(doorMove.OffsetZ, -DoorRetract) && Near(doorMove.OffsetY, -DoorDrop), "门开：托板贴门洞、门收进门腔");
            double carrierZ = carrier.TransformToAncestor(port).Transform(new Point3D()).Z;
            Check(Near(carrierZ, 0), "花篮跟着托板推进去了");

            // 6. 卸载：先关门、再退出
            scene.IsDocked = false;
            scene.IsDoorOpen = false;
            Pump(250);
            Check(Door() > 0.15 && Door() < 0.85 && Near(Dock(), 1) && Lit(door) && !Lit(carriage), "0.25 s：门关到一半、托板没动，只亮门");
            Pump(400);
            Check(Near(Door(), 0) && Dock() < 1 && Dock() > 0.4 && Lit(carriage), "0.65 s：门关好了、托板开始退");
            Pump(650);
            Check(Near(Dock(), 0) && Near(carrier.TransformToAncestor(port).Transform(new Point3D()).Z, DockTravel) && !port.HasAnimatedProperties,
                "1.3 s：退出到位，花篮跟着退出来");

            // 7. 中途反向：推到一半又要退，从当前位置往回走，不跳
            scene.IsDocked = true;
            Pump(200);
            double halfway = Dock();
            scene.IsDocked = false;
            Check(Math.Abs(Dock() - halfway) < 0.02, "中途反向从当前位置接着走");
            Pump(500);
            Check(Near(Dock(), 0), "反向退回到位");

            // 8. 门在半路（开到位、关到位都灭）：推进去、门停在行程中间，一直亮；到位后走到那一头、撤亮
            scene.IsDocked = true;
            scene.IsDoorUnknown = true;
            Pump(1000);
            Check(Near(Dock(), 1) && Near(Door(), 0.5) && Lit(door) && !port.HasAnimatedProperties, "门在半路：停在中间、一直亮");
            scene.IsDoorUnknown = false;
            scene.IsDoorOpen = true;
            Pump(500);
            Check(Near(Door(), 1) && !Lit(door), "到位了：门开到底、撤亮");

            // 9. 页面藏起来直接落到目标，不留动画；还没挂进视口的直接画目标
            scene.IsDocked = false;
            scene.IsDoorOpen = false;
            Pump(60);
            scene.Visibility = Visibility.Hidden;
            Pump(30);
            Check(Near(Dock(), 0) && Near(Door(), 0) && !port.HasAnimatedProperties && !Lit(door) && !Lit(carriage), "藏起来：直接落到退出、门关");
            scene.Visibility = Visibility.Visible;
            var detached = new LoadPortVisual3D { IsDocked = true, IsDoorOpen = true };
            var detachedDoor = (TranslateTransform3D)((HardwareVisual3D)detached.Children[0]).Transform;
            Check(Near(detachedDoor.OffsetY, -DoorDrop) && !detached.HasAnimatedProperties, "还没挂进视口：直接画成门开");

            // 10. 面板灯：在位、放置到位亮绿；AUTO 亮绿 / MANUAL 亮黄（二选一）
            scene.IsPresent = true;
            scene.IsPlaced = null;
            scene.IsAutoMode = false;
            Pump(20);
            Check(LampOn(port, "_presentLamp") && !LampOn(port, "_placedLamp") && !LampOn(port, "_autoLamp") && LampOn(port, "_manualLamp"),
                "在位亮、到位反馈不可用灭、Manual 亮 MANUAL");
            scene.IsAutoMode = true;
            Pump(20);
            Check(LampOn(port, "_autoLamp") && !LampOn(port, "_manualLamp"), "Auto 亮 AUTO、灭 MANUAL");

            // 11. 视角工具栏：俯视从上往下看、背面从设备里面那侧看、默认视角复原
            Point3D defaultPosition = camera.Position;
            Click(scene, "TopViewButton");
            Check(camera.Position.Y > defaultPosition.Y + 3, "俯视：相机抬高");
            Click(scene, "BackViewButton");
            Check(camera.Position.Z < 0, "背面：相机转到背板后面");
            Click(scene, "DefaultViewButton");
            Check((camera.Position - defaultPosition).Length < 1e-6, "默认视角复原");

            // 12. 卸下载具：花篮拿掉、片数和空槽不写
            scene.IsCarrierPresent = false;
            Pump(20);
            Check(Carrier() is null && !Shown("Counter"), "载具拿走：花篮卸下");

            // 13. 手动页的显示模型：设备没有对接反馈，按状态和门推；门跟开到位 / 关到位走
            var idle = new LoadPortModel { State = 30, IsDoorOpen = false, IsDoorClosed = true };
            Check(!idle.IsDocked && !idle.IsDoorShownOpen && !idle.IsDoorUnknown, "空闲、门关到位：退出、门关");
            var loading = new LoadPortModel { State = 100, IsDoorOpen = false, IsDoorClosed = true };
            Check(loading.IsDocked && !loading.IsDoorShownOpen, "装载中、门还关着：先推进去");
            var loaded = new LoadPortModel { State = 110, IsDoorOpen = true, IsDoorClosed = false };
            Check(loaded.IsDocked && loaded.IsDoorShownOpen, "已装载、门开到位：推进去、门开");
            var moving = new LoadPortModel { State = 120, IsDoorOpen = false, IsDoorClosed = false };
            Check(moving.IsDocked && moving.IsDoorUnknown && !moving.IsDoorShownOpen, "卸载中、门在半路：还推着，门画中间");
            var unloadingClosed = new LoadPortModel { State = 120, IsDoorOpen = false, IsDoorClosed = true };
            Check(!unloadingClosed.IsDocked, "卸载中、门关到位了：画退出（先关门再退）");
            var noFeedback = new LoadPortModel { State = 110 };
            Check(noFeedback.IsDocked && noFeedback.IsDoorShownOpen && !noFeedback.IsDoorUnknown, "查不到门反馈：已装载按门开画");

            if (args.Length > 0)
            {
                scene.IsCarrierPresent = true;
                scene.IsDocked = true;
                scene.IsDoorOpen = true;
                Pump(1200);
                var bitmap = new RenderTargetBitmap(440, 560, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(scene);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string output = Path.GetFullPath(args[0]);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var file = File.Create(output);
                encoder.Save(file);
                Console.WriteLine($"Preview: {output}");
            }

            host.RootVisual = null;
            app.Shutdown();
            Console.WriteLine($"PASS: {_checks} LoadPort scene checks (sc slot count, Disk wafers, selection, 0.5 s ordered motions, highlight, unknown door, lamps, views, model mapping).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static List<WaferModel> Wafers(int slots, int[] empty)
    {
        return Enumerable.Range(1, slots).Where(slot => !empty.Contains(slot))
            .Select(slot => new WaferModel { Slot = slot, LpSlot = slot.ToString("00"), State = "IdleHasjob" }).ToList();
    }

    private static List<DiskVisual3D> Disks(CarrierVisual3D carrier)
    {
        return carrier.Children.OfType<DiskVisual3D>().ToList();
    }

    /// <summary>槽号是自发光的小方片。</summary>
    private static int Labels(CarrierVisual3D carrier)
    {
        return ((Model3DGroup)carrier.Content).Children.OfType<GeometryModel3D>().Count(model => model.Material is EmissiveMaterial);
    }

    private static double Height(CarrierVisual3D carrier)
    {
        return ((Model3DGroup)carrier.Content).Bounds.SizeY;
    }

    private static Color ColorOf(HardwareVisual3D part)
    {
        var mesh = ((Model3DGroup)part.Content).Children.OfType<GeometryModel3D>().First();
        return ((SolidColorBrush)((DiffuseMaterial)mesh.Material).Brush).Color;
    }

    /// <summary>亮着的灯是"漫反射 + 自发光"一组材质，灭着的只有漫反射。</summary>
    private static bool LampOn(LoadPortVisual3D port, string field)
    {
        var lamp = (GeometryModel3D)typeof(LoadPortVisual3D).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(port)!;
        return lamp.Material is MaterialGroup;
    }

    private static void Click(LoadPortScene scene, string button)
    {
        ((Button)scene.FindName(button)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    /// <summary>点一下（按下到抬起没挪）：走场景自己的选片。</summary>
    private static void Select(LoadPortScene scene, Point point)
    {
        typeof(LoadPortScene).GetMethod("SelectWaferAt", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(scene, [point]);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAIL: " + description);
        }

        _checks++;
    }
}
