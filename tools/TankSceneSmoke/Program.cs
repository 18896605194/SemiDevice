using System.Diagnostics;
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
using System.Windows.Threading;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

// 供液槽三维图冒烟：TankScene（透明圆筒 TankVisual3D + 液位信号灯 SignalLamp）绑 TankModel（后端推的 TankDto）。
// 液面跟液位：七个液位的液面高度、液体颜色、右上角的字；液面 0.2 s 过渡、中途换目标落在最后一个、藏着直接落位、液位不知道不画液体；
// 信号灯跟各自的 DI 信号：亮灭、名字照推送，读不到是"不知道"色，信号对不上时液位不知道、灯照样各亮各的，sc 里没配的不画；换槽改绑；
// 罐壁透明度（依赖属性，外部设或绑定；图上没有滑块）、默认视角 / 正视图、滚轮缩放、前后两半罐壁跟着相机分、信号灯在图里且在罐右边；中英文的字。不连后端。
internal static class Program
{
    private const int SceneWidth = 640;
    private const int SceneHeight = 620;

    private static readonly string[] LampNames = ["LowLowSignal", "LowSignal", "HighSignal", "HighHighSignal", "OverfillSignal"];

    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var bindingErrors = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

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
                app.Resources.MergedDictionaries.Add(Dictionary(name));
            }

            // 0. 信号灯本身：触发亮绿、报警位触发亮红、没触发灭、读不到是"不知道"色，名字原样
            var single = new SignalLamp { Text = "Feedback" };
            Check(LampTag(single) == "Unknown" && LampText(single) == "Feedback", "信号灯默认读不到：不知道色，名字原样显示");
            single.IsActive = true;
            Check(LampTag(single) == "On", "触发：亮绿");
            single.IsAlarm = true;
            Check(LampTag(single) == "Alarm", "报警位触发：亮红");
            single.IsActive = false;
            Check(LampTag(single) == "Off", "没触发：灭（报警位也一样）");
            single.IsActive = null;
            Check(LampTag(single) == "Unknown", "又读不到：回到不知道色");

            // 靠左上：改小尺寸出图时不居中偏移
            var scene = new TankScene
            {
                Width = SceneWidth,
                Height = SceneHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            using var host = new HwndSource(new HwndSourceParameters("Tank scene smoke")
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                PositionX = -32000, PositionY = -32000, Width = SceneWidth, Height = SceneHeight
            });
            host.RootVisual = scene;
            Pump(60);
            var body = (TankVisual3D)scene.FindName("Body");
            var camera = (OrthographicCamera)scene.FindName("Camera");
            var stage = (Grid)scene.FindName("Stage");
            var model = (Model3DGroup)body.Content;
            var rearWall = (GeometryModel3D)model.Children[0];
            var liquidSlot = (Model3DGroup)model.Children[2];
            var frontWall = (GeometryModel3D)model.Children[3];
            string LevelText() => ((TextBlock)scene.FindName("LevelText")).Text;
            Color LevelDot() => ((SolidColorBrush)((Border)scene.FindName("LevelDot")).Background).Color;
            Color Token(string key) => ((SolidColorBrush)app.FindResource(key)).Color;

            // 1. 没给槽：液位不知道，不画液体、一盏灯都不画，右上角写液位未知（不兜底成正常）；给了槽还没收到推送也一样
            Check(scene.Tank is null && body.FillFraction == 0 && liquidSlot.Children.Count == 0, "没给槽：液位不知道，不画液体");
            Check(Lamps(scene) == "-,-,-,-,-", "没给槽：灯都不画");
            Check(LevelText() == "液位未知" && LevelDot() == Token("DarkDisabledText"), "右上角写液位未知、灰点");
            var tank = new TankModel();
            scene.Tank = tank;
            Pump(20);
            Check(Lamps(scene) == "-,-,-,-,-" && LevelText() == "液位未知", "槽还没收到推送：不知道配了哪几个信号，灯都不画");

            // 2. 七个液位：液面落在上下两个信号点中间，液体到高高变红；灯照推来的各个信号亮（HH、Overfill 是报警位，亮红）
            Expect(scene, body, tank, Dto(TankLevel.LL, false, false, false, false, false), 0.05, "Off,Off,Off,Off,Off", false, "低低液位", "DarkWarningStatus");
            Check(string.Join(",", LampNames.Select(name => LampText((SignalLamp)scene.FindName(name)))) == "LL,L,H,HH,Overfill",
                "灯的名字照推送里的信号名（sc 的节点名）");
            Expect(scene, body, tank, Dto(TankLevel.L, true, false, false, false, false), 0.205, "On,Off,Off,Off,Off", false, "低液位", "DarkWarningStatus");
            Expect(scene, body, tank, Dto(TankLevel.Normal, true, true, false, false, false), 0.47, "On,On,Off,Off,Off", false, "液位正常", "DarkRunningStatus");
            Expect(scene, body, tank, Dto(TankLevel.H, true, true, true, false, false), 0.72, "On,On,On,Off,Off", false, "高液位", "DarkWarningStatus");
            Expect(scene, body, tank, Dto(TankLevel.HH, true, true, true, true, false), 0.865, "On,On,On,Alarm,Off", true, "高高液位", "DarkAlarmStatus");
            Expect(scene, body, tank, Dto(TankLevel.Overfill, true, true, true, true, true), 0.998, "On,On,On,Alarm,Alarm", true, "溢流", "DarkAlarmStatus");
            Check(liquidSlot.Children.Count == 1, "有液位：液体挂上");
            Check(LiquidColor(liquidSlot) == ((Color)app.FindResource("DarkHardwareLiquidAlarmColor")), "高高 / 溢流时液体是报警红（token）");
            Expect(scene, body, tank, Dto(TankLevel.Unknown, null, null, null, null, null), 0, "Unknown,Unknown,Unknown,Unknown,Unknown", false, "液位未知", "DarkDisabledText");
            Check(liquidSlot.Children.Count == 0, "液位不知道：液体拿掉，不编一个高度");
            tank.Update(Dto(TankLevel.Normal, true, true, false, false, false));
            Pump(320);
            Check(LiquidColor(liquidSlot) == ((Color)app.FindResource("DarkHardwareLiquidColor")), "回到正常液体变回蓝（token）");

            // 3. 灯跟各自的 DI，不从液位推：信号对不上时液位不知道，灯照样各亮各的，看得出是哪个点不对
            Expect(scene, body, tank, Dto(TankLevel.Unknown, true, false, true, false, false), 0, "On,Off,On,Off,Off", false, "液位未知", "DarkDisabledText");
            Expect(scene, body, tank, Dto(TankLevel.Unknown, true, null, false, false, false), 0, "On,Unknown,Off,Off,Off", false, "液位未知", "DarkDisabledText");
            Expect(scene, body, tank, Dto(TankLevel.Overfill, null, true, true, true, true), 0.998, "Unknown,On,On,Alarm,Alarm", true, "溢流", "DarkAlarmStatus");
            tank.Update(Dto(TankLevel.Normal, true, true, false, false, false));
            Pump(320);

            // 4. 液面过渡 0.2 s：中途在两档之间；中途换目标落在最后一个；藏着时直接落位
            tank.Update(Dto(TankLevel.H, true, true, true, false, false));
            Pump(70);
            Check(body.FillFraction > 0.47 && body.FillFraction < 0.72, "显示着换液位：液面在过渡");
            Pump(300);
            Check(Near(body.FillFraction, 0.72), "0.2 s 后到位");
            tank.Update(Dto(TankLevel.Overfill, true, true, true, true, true));
            Pump(60);
            tank.Update(Dto(TankLevel.L, true, false, false, false, false));
            Pump(320);
            Check(Near(body.FillFraction, 0.205) && !body.IsAlarm && Lamps(scene) == "On,Off,Off,Off,Off", "中途换目标：落在最后一个液位，液体不留红");
            scene.Visibility = Visibility.Collapsed;
            Pump(20);
            tank.Update(Dto(TankLevel.HH, true, true, true, true, false));
            Check(Near(body.FillFraction, 0.865), "藏着：直接落位不过渡");
            scene.Visibility = Visibility.Visible;
            tank.Update(Dto(TankLevel.Normal, true, true, false, false, false));
            Pump(320);

            // 5. sc 里只配了 LL、H：只画这两盏，位置还是各自的高度；换一个槽灯改绑新的，旧槽再变不影响图
            var partial = new TankModel();
            partial.Update(new TankDto
            {
                Path = "CDS.IPA.Tank2",
                Level = (int)TankLevel.L,
                LL = new DiSensorDto { Name = "LL", IsActive = true },
                H = new DiSensorDto { Name = "H", IsActive = false }
            });
            scene.Tank = partial;
            Pump(60);
            Check(Lamps(scene) == "On,-,Off,-,-", "只配了 LL、H：只画这两盏，跟各自的信号");
            Check(Near(body.FillFraction, 0.205) && LevelText() == "低液位", "换槽：液面、右上角跟新槽，直接落位");
            CheckLampLayout(scene, stage, "LL、H 两盏");
            tank.Update(Dto(TankLevel.Overfill, true, true, true, true, true));
            Pump(320);
            Check(Lamps(scene) == "On,-,Off,-,-" && Near(body.FillFraction, 0.205), "换下来的旧槽再变：图不跟");
            partial.Update(Dto(TankLevel.Normal, true, true, false, false, false));
            Pump(320);
            Check(Lamps(scene) == "On,On,Off,Off,Off", "推送里信号配全了：五盏都画出来");
            scene.Tank = tank;
            tank.Update(Dto(TankLevel.Normal, true, true, false, false, false));
            Pump(320);

            // 6. 罐壁透明度：图上没有滑块，外部改依赖属性（直接设、绑定都行）传给三维件；前壁比后壁实一点；超出 0～1 不收
            Check(scene.FindName("TransparencySlider") is null, "图上不放透明度滑块");
            Check(Near(scene.WallTransparency, 0.72) && Near(body.WallTransparency, 0.72), "默认透明度 0.72");
            scene.WallTransparency = 0.9;
            Pump(20);
            Check(Near(body.WallTransparency, 0.9), "外部设依赖属性：三维件跟着");
            var source = new TransparencySource { Value = 0.35 };
            BindingOperations.SetBinding(scene, TankScene.WallTransparencyProperty, new Binding(nameof(TransparencySource.Value)) { Source = source });
            Pump(20);
            Check(Near(scene.WallTransparency, 0.35) && Near(body.WallTransparency, 0.35), "外部绑定依赖属性：三维件跟着");
            Check(WallAlpha(frontWall) == (byte)Math.Round(255 * 0.65 * 0.65) && WallAlpha(rearWall) == (byte)Math.Round(255 * 0.65 * 0.35),
                "前半圈罐壁比后半圈实（透明度 0.35）");
            bool rejected = false;
            try
            {
                scene.WallTransparency = 1.5;
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Check(rejected && Near(scene.WallTransparency, 0.35), "透明度超出 0～1 不收");
            scene.WallTransparency = 0.72;

            // 7. 视角：默认斜着看、正视图接近水平；前后两半罐壁跟着相机方位分；滚轮缩放有上下限
            Click(scene, "FrontViewButton");
            Pump(20);
            Check(Math.Abs(camera.LookDirection.Y / camera.LookDirection.Length) < 0.1 && Near(body.Azimuth, 0), "正视图：几乎水平看，前壁朝正前");
            Click(scene, "DefaultViewButton");
            Pump(20);
            double defaultWidth = camera.Width;
            Check(camera.LookDirection.Y < 0 && Near(body.Azimuth, -18), "默认视角：斜上方看，方位 -18 度");
            Check(FacesCamera(frontWall, camera) && !FacesCamera(rearWall, camera), "前半圈罐壁朝相机、后半圈背着相机");
            for (int i = 0; i < 10; i++)
            {
                Wheel(stage, 120);
            }

            Pump(20);
            double zoomedIn = camera.Width;
            Check(zoomedIn < defaultWidth && Near(zoomedIn, defaultWidth / 1.08), "滚轮放大，最多 1.08 倍");
            for (int i = 0; i < 20; i++)
            {
                Wheel(stage, -120);
            }

            Pump(20);
            Check(Near(camera.Width, defaultWidth / 0.72), "滚轮缩小，最少 0.72 倍");
            Click(scene, "DefaultViewButton");
            Pump(20);
            Check(Near(camera.Width, defaultWidth), "点默认视角缩放也复原");

            // 8. 信号灯都在图里、在罐的右边，从下往上 LL、L、H、HH、Overfill；图小了也放得下
            CheckLampLayout(scene, stage, "640×620");
            scene.Width = 420;
            scene.Height = 380;
            Pump(60);
            CheckLampLayout(scene, stage, "420×380");
            scene.Width = SceneWidth;
            scene.Height = SceneHeight;
            Pump(60);

            // 9. 罐沿是三维件的金属色（token），英文界面的字
            Check(body.BodyColor == (Color)app.FindResource("DarkHardwareTankRimColor"), "罐沿颜色取 token");
            Check(bindingErrors.Count == 0, $"没有绑定错误（{bindingErrors.Count}）");

            string? output = null;
            if (args.Length > 0)
            {
                output = Path.GetFullPath(args[0]);
            }

            if (output is not null)
            {
                Directory.CreateDirectory(output);
                Shots(scene, tank, output, "zh");
            }

            // 客户端开机定语言：换成英文包后新建一张图（挂在 HwndSource 上的不跟着应用资源刷新）
            app.Resources.MergedDictionaries.Remove(app.Resources.MergedDictionaries.Last());
            app.Resources.MergedDictionaries.Add(Dictionary("Localization/Strings.en-US"));
            var englishTank = new TankModel();
            englishTank.Update(Dto(TankLevel.HH, true, true, true, true, false));
            var english = new TankScene
            {
                Width = SceneWidth,
                Height = SceneHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Tank = englishTank
            };
            host.RootVisual = english;
            Pump(320);
            Check(((TextBlock)english.FindName("LevelText")).Text == "High-high level", "英文界面：右上角写 High-high level");
            if (output is not null)
            {
                Shots(english, englishTank, output, "en");
                Console.WriteLine($"Preview: {output}");
            }

            host.RootVisual = null;
            app.Shutdown();
            Console.WriteLine($"PASS: {_checks} tank scene checks (signal lamp, levels, lamps from each DI signal, unconfigured lamps hidden, tank swap, "
                + "liquid color, 0.2 s fill, hidden settle, wall transparency, views, zoom, lamp layout, language).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>
    /// 五个信号都配了的推送
    /// </summary>
    private static TankDto Dto(TankLevel level, bool? ll, bool? l, bool? h, bool? hh, bool? overfill)
    {
        return new TankDto
        {
            Path = "CDS.IPA.Tank1",
            Level = (int)level,
            LL = new DiSensorDto { Name = "LL", IsActive = ll },
            L = new DiSensorDto { Name = "L", IsActive = l },
            H = new DiSensorDto { Name = "H", IsActive = h },
            HH = new DiSensorDto { Name = "HH", IsActive = hh },
            Overfill = new DiSensorDto { Name = "Overfill", IsActive = overfill }
        };
    }

    private static void Expect(TankScene scene, TankVisual3D body, TankModel tank, TankDto dto, double fraction, string lamps, bool alarm, string text, string dot)
    {
        tank.Update(dto);
        Pump(320);
        var level = (TankLevel)dto.Level;
        string actualLamps = Lamps(scene);
        Color dotColor = ((SolidColorBrush)((Border)scene.FindName("LevelDot")).Background).Color;
        Check(Near(body.FillFraction, fraction), $"{level}：液面 {fraction}（实际 {body.FillFraction}）");
        Check(actualLamps == lamps, $"{level}：信号灯 {lamps}（实际 {actualLamps}）");
        string color = "不红";
        if (alarm)
        {
            color = "红";
        }

        Check(body.IsAlarm == alarm, $"{level}：液体{color}");
        Check(((TextBlock)scene.FindName("LevelText")).Text == text, $"{level}：右上角写 {text}");
        Check(dotColor == ((SolidColorBrush)Application.Current.FindResource(dot)).Color, $"{level}：点是 {dot}");
    }

    /// <summary>
    /// 从下往上五盏灯的颜色，不画的写 -
    /// </summary>
    private static string Lamps(TankScene scene)
    {
        return string.Join(",", LampNames.Select(name =>
        {
            var lamp = (SignalLamp)scene.FindName(name);
            if (lamp.Visibility != Visibility.Visible)
            {
                return "-";
            }

            return LampTag(lamp);
        }));
    }

    private static string LampTag(SignalLamp lamp)
    {
        return (string)((Border)lamp.FindName("Lamp")).Tag;
    }

    private static string LampText(SignalLamp lamp)
    {
        return ((TextBlock)lamp.FindName("NameText")).Text;
    }

    /// <summary>
    /// 画出来的灯都在图里、在罐右边沿外，从下往上排
    /// </summary>
    private static void CheckLampLayout(TankScene scene, Grid stage, string size)
    {
        var rows = LampNames
            .Select(name => (FrameworkElement)scene.FindName(name))
            .Where(row => row.Visibility == Visibility.Visible)
            .Select(row => new Rect(Canvas.GetLeft(row), Canvas.GetTop(row), row.ActualWidth, row.ActualHeight)).ToList();
        Check(rows.Count > 0 && rows.All(rect => rect.Width > 0 && rect.Left >= 0 && rect.Top >= 0 && rect.Right <= stage.ActualWidth && rect.Bottom <= stage.ActualHeight),
            $"{size}：灯连名字都在图里");
        Check(rows.Zip(rows.Skip(1)).All(pair => pair.Second.Top < pair.First.Top), $"{size}：从下往上 LL、L、H、HH、Overfill");
        Check(rows.All(rect => rect.Left > stage.ActualWidth / 2), $"{size}：灯在罐的右边");
    }

    private static void Shots(TankScene scene, TankModel tank, string folder, string language)
    {
        tank.Update(Dto(TankLevel.Normal, true, true, false, false, false));
        Click(scene, "DefaultViewButton");
        Pump(400);
        Save(scene, Path.Combine(folder, $"tank-{language}-default.png"));
        Click(scene, "FrontViewButton");
        Pump(100);
        Save(scene, Path.Combine(folder, $"tank-{language}-front.png"));
        tank.Update(Dto(TankLevel.Overfill, true, true, true, true, true));
        Click(scene, "DefaultViewButton");
        Pump(400);
        Save(scene, Path.Combine(folder, $"tank-{language}-overfill.png"));
        tank.Update(Dto(TankLevel.Unknown, true, false, true, false, false));
        Pump(100);
        Save(scene, Path.Combine(folder, $"tank-{language}-mismatch.png"));
        var partial = new TankModel();
        partial.Update(new TankDto
        {
            Level = (int)TankLevel.L,
            LL = new DiSensorDto { Name = "LL", IsActive = true },
            H = new DiSensorDto { Name = "H", IsActive = false }
        });
        scene.Tank = partial;
        Pump(100);
        Save(scene, Path.Combine(folder, $"tank-{language}-partial.png"));
        scene.Tank = tank;
        tank.Update(Dto(TankLevel.L, true, false, false, false, false));
        scene.Width = 420;
        scene.Height = 380;
        Pump(400);
        Save(scene, Path.Combine(folder, $"tank-{language}-compact.png"));
        scene.Width = SceneWidth;
        scene.Height = SceneHeight;
        Pump(60);
    }

    private static void Save(TankScene scene, string path)
    {
        scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)scene.ActualWidth, (int)scene.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static ResourceDictionary Dictionary(string name)
    {
        return new ResourceDictionary { Source = new Uri($"/xyz.Client.Presentation;component/{name}.xaml", UriKind.Relative) };
    }

    /// <summary>
    /// 液体侧面的漫反射颜色
    /// </summary>
    private static Color LiquidColor(Model3DGroup liquidSlot)
    {
        var liquid = (Model3DGroup)liquidSlot.Children[0];
        var side = (GeometryModel3D)liquid.Children[0];
        var diffuse = ((MaterialGroup)side.Material).Children.OfType<DiffuseMaterial>().First();
        return ((SolidColorBrush)diffuse.Brush).Color;
    }

    private static byte WallAlpha(GeometryModel3D wall)
    {
        var diffuse = ((MaterialGroup)wall.Material).Children.OfType<DiffuseMaterial>().First();
        return ((SolidColorBrush)diffuse.Brush).Color.A;
    }

    /// <summary>
    /// 这半圈罐壁正中那一点的法线朝不朝相机
    /// </summary>
    private static bool FacesCamera(GeometryModel3D wall, OrthographicCamera camera)
    {
        var mesh = (MeshGeometry3D)wall.Geometry;
        Vector3D normal = mesh.Normals[mesh.Normals.Count / 2];
        return Vector3D.DotProduct(normal, camera.LookDirection) < 0;
    }

    private static void Click(TankScene scene, string button)
    {
        ((Button)scene.FindName(button)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static void Wheel(UIElement target, int delta)
    {
        target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.MouseWheelEvent });
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

/// <summary>
/// 外部绑透明度用的数据源（相当于页面的 VM）
/// </summary>
internal sealed class TransparencySource
{
    public double Value { get; set; }
}

/// <summary>
/// 数绑定错误
/// </summary>
internal sealed class BindingErrorListener : TraceListener
{
    public int Count { get; private set; }

    public override void Write(string? message)
    {
    }

    public override void WriteLine(string? message)
    {
        Count++;
        Console.Error.WriteLine("Binding: " + message);
    }
}
