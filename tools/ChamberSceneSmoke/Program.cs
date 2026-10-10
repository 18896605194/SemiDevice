using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using xyz.Client.Common.Log;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

// 腔体三维图冒烟：从后端的设备推送（ChamberPartsDto 的轴表、气缸表、喷嘴表）挑出门、Bowl、卡盘、摆臂（Lift、喷嘴）并搭建、摆臂工艺位对盘心 / Home 对接液杯、
// 液柱落点、0.2 s 过渡（中途换目标不跳、隐藏时直接落位）、Lift 升降带动摆臂和液柱、门 / Bowl / Lift 跟到位反馈（未知停在行程中间并高亮）、
// 旋转跟状态、组成变化重搭、多于两条摆臂只画两条、视角工具栏（默认视角、俯视）、解绑。不连后端，推送直接喂显示模型。
internal static class Program
{
    private const double HomeAngle = -90;
    private const double LiftStroke = 0.55;
    private const double CupLandingHeight = 0.41;
    private const double SurfaceGap = 0.001;

    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application();
            // 跟壳的 App.xaml 一样先并 MaterialDesign 主题、再并框架样式和中文语言包：工具栏按钮的样式基于 MaterialDesign，
            // 按钮字、日志里的提示才是按模板拼好的句子。
            app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
            {
                BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark,
                PrimaryColor = MaterialDesignColors.PrimaryColor.Blue,
                SecondaryColor = MaterialDesignColors.SecondaryColor.Lime,
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml") });
            foreach (string name in new[] { "Styles/Color", "Styles/DarkColors", "Styles/FontSize", "Styles/BorderStyles", "Styles/ButtonStyles",
                         "Localization/Strings.zh-CN" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/xyz.Client.Presentation;component/{name}.xaml", UriKind.Relative) });
            }

            var scene = new ChamberScene { Width = 900, Height = 600 };
            using var host = new HwndSource(new HwndSourceParameters("Chamber scene smoke")
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                PositionX = -32000, PositionY = -32000, Width = 900, Height = 600
            });
            host.RootVisual = scene;
            Pump(60);
            var plate = (ChamberBaseVisual3D)scene.FindName("Base");

            // 1. 还没有设备组成：只画旋转盘
            Check(plate.Attachments.Count == 1 && plate.Attachments[0] is DiskVisual3D, "没收到设备组成时只画旋转盘");
            var disk = (DiskVisual3D)plate.Attachments[0];

            // 2. 从通用推送里认设备：门（腔体下叫 Door 的气缸）、Bowl（Bowl 开头的第一个气缸）、卡盘、两条摆臂各一套 Lift / 摆臂（两路喷嘴管）/ 接液杯
            var devices = new ChamberDeviceDataModel();
            scene.Devices = devices;
            var state = State(arms: 2);
            state.Arms[0].Reach = 1;
            state.Arms[1].Reach = 1;
            devices.Update(state.ToDto());
            Pump(30);
            var bowl = plate.Attachments.OfType<BowlVisual3D>().Single();
            var door = plate.Attachments.OfType<DoorVisual3D>().Single();
            var arms = plate.Attachments.OfType<ArmVisual3D>().ToList();
            var lifts = plate.Attachments.OfType<LiftVisual3D>().ToList();
            var cups = plate.Attachments.OfType<HomeCupVisual3D>().ToList();
            Check(plate.Attachments.Count == 9 && arms.Count == 2 && lifts.Count == 2 && cups.Count == 2,
                "门、Bowl、旋转盘 + 两套 Lift / 摆臂 / 接液杯");
            Check(arms.All(arm => arm.Attachments.OfType<FluidPipeVisual3D>().Count() == 2), "每条摆臂两路喷嘴两根管");
            Check(Pivot(arms[0], plate).X > bowl.Radius && Pivot(arms[1], plate).X < -bowl.Radius, "第 1 条装在 Bowl 右侧，第 2 条在左侧");
            Check(devices.Revision == 1 && devices.Module == "Chamber1" && devices.Door.Path == "Chamber1.Door"
                && devices.Bowl.Path == "Chamber1.Bowl1" && devices.Spin.Path == "Chamber1.SpinMotor", "门按名字、Bowl 按名字开头、卡盘按类名认");
            Check(devices.Arms.Select(arm => arm.Path).SequenceEqual(["Chamber1.Arm1", "Chamber1.Arm2"])
                && devices.Arms[0].Lift.Path == "Chamber1.Arm1.Lift"
                && devices.Arms[1].Nozzles.Select(nozzle => nozzle.Path).SequenceEqual(["Chamber1.Arm2.Nozzle_DIW", "Chamber1.Arm2.Nozzle_SC1"]),
                "摆臂按类名认，它下面的气缸是 Lift、阀按 sc 先后是喷嘴");

            // 3. 工艺位：两路喷嘴的中点正对盘心，液柱落到盘面
            var center = disk.TransformToAncestor(plate).Transform(new Point3D());
            double diskTop = disk.TransformToAncestor(plate).Transform(new Point3D(0, disk.Thickness + SurfaceGap, 0)).Y;
            foreach (var arm in arms)
            {
                var pipes = Pipes(arm);
                var middle = Middle(Nozzle(pipes[0], plate), Nozzle(pipes[1], plate));
                Check(Horizontal(middle, center) < 1e-6, "工艺位两路喷嘴中点正对盘心");
                Check(pipes.All(pipe => Near(Landing(pipe, plate).Y, diskTop)), "工艺位液柱落到盘面");
            }
            double processAngle = arms[0].Angle;
            Check(!arms[0].HasAnimatedProperties, "刚搭好直接摆到目标角度，不从默认位置动画过来");

            // 4. 回 Home：0.2 s 过渡；到 Home 喷嘴在自己的接液杯正上方，液柱落进杯里，有出液时杯亮
            state = State(arms: 2);
            state.Arms[0].Nozzles[0].IsOn = true;
            devices.Update(state.ToDto());
            Check(arms[0].HasAnimatedProperties && arms[1].HasAnimatedProperties, "收到新位置开始过渡");
            Pump(120);
            Check(arms[0].Angle > processAngle && arms[0].Angle < HomeAngle, "过渡中角度在两头之间");
            Pump(350);
            Check(Near(arms[0].Angle, HomeAngle) && !arms[0].HasAnimatedProperties, "约 0.2 s 到位，不留动画时钟");
            for (int i = 0; i < arms.Count; i++)
            {
                var cupTop = cups[i].TransformToAncestor(plate).Transform(new Point3D(0, cups[i].Height, 0));
                double cupLanding = cups[i].TransformToAncestor(plate).Transform(new Point3D(0, CupLandingHeight, 0)).Y;
                foreach (var pipe in Pipes(arms[i]))
                {
                    Check(Horizontal(Nozzle(pipe, plate), cupTop) < cups[i].Radius * 0.7 * 0.8, "Home 喷嘴在自己的接液杯口内");
                    Check(Near(Landing(pipe, plate).Y, cupLanding), "Home 液柱落进接液杯");
                }
            }
            Check(cups[0].IsDraining && !cups[1].IsDraining, "在 Home 且出液的那条臂接液杯亮，另一条不亮");

            // 5. 中途换目标：从当前显示位置接着走，不跳
            state = State(arms: 2);
            state.Arms[0].Reach = 1;
            devices.Update(state.ToDto());
            Pump(100);
            double midway = arms[0].Angle;
            state = State(arms: 2);
            state.Arms[0].Reach = 0.5;
            devices.Update(state.ToDto());
            Check(Near(arms[0].Angle, midway), "中途换目标不跳");
            Pump(450);
            Check(Near(arms[0].Angle, HomeAngle + 0.5 * (processAngle - HomeAngle)) && !arms[0].HasAnimatedProperties, "Reach 0.5 落在 Home 和工艺位中间");

            // 5b. 示教过边缘：Home → 边缘 → 中心分两段；轴在 Edge 时喷嘴正好在盘边（靠 Home 那侧），只改边缘也会重新摆
            state = State(arms: 2);
            state.Arms[0].Reach = 0.5;
            state.Arms[0].EdgeReach = 0.5;
            devices.Update(state.ToDto());
            Pump(450);
            double edgeAngle = arms[0].Angle;
            var edgeMiddle = Middle(Nozzle(Pipes(arms[0])[0], plate), Nozzle(Pipes(arms[0])[1], plate));
            Check(!Near(edgeAngle, HomeAngle + 0.5 * (processAngle - HomeAngle)), "只改边缘位置也重新摆");
            Check(Math.Abs(Horizontal(edgeMiddle, center) - disk.Radius) < 1e-6, "轴在 Edge：两路喷嘴中点正好在盘边");
            Check(edgeAngle < HomeAngle && edgeAngle > processAngle, "边缘在 Home 和工艺位之间（第一个边缘）");
            state = State(arms: 2);
            state.Arms[0].Reach = 0.25;
            state.Arms[0].EdgeReach = 0.5;
            devices.Update(state.ToDto());
            Pump(450);
            Check(Near(arms[0].Angle, (HomeAngle + edgeAngle) / 2), "Home 到边缘之间按轴位置线性");
            state = State(arms: 2);
            state.Arms[0].Reach = 0.75;
            state.Arms[0].EdgeReach = 0.5;
            devices.Update(state.ToDto());
            Pump(450);
            Check(Near(arms[0].Angle, (edgeAngle + processAngle) / 2), "边缘到中心之间按轴位置线性");
            state = State(arms: 2);
            state.Arms[0].Reach = 1;
            state.Arms[0].EdgeReach = 0.5;
            devices.Update(state.ToDto());
            Pump(450);
            var centerMiddle = Middle(Nozzle(Pipes(arms[0])[0], plate), Nozzle(Pipes(arms[0])[1], plate));
            Check(Near(arms[0].Angle, processAngle) && Horizontal(centerMiddle, center) < 1e-6, "轴在 Center 仍正对盘心");
            devices.Update(State(arms: 2).ToDto());
            Pump(450);

            // 6. 页面隐藏：收到新位置直接落位；过渡中被隐藏也直接落到目标
            scene.Visibility = Visibility.Hidden;
            Pump(30);
            state = State(arms: 2);
            state.Arms[0].Reach = 1;
            devices.Update(state.ToDto());
            Check(Near(arms[0].Angle, processAngle) && !arms[0].HasAnimatedProperties, "隐藏时直接落位、不起动画");
            scene.Visibility = Visibility.Visible;
            Pump(30);
            devices.Update(State(arms: 2).ToDto());
            Pump(100);
            Check(arms[0].HasAnimatedProperties, "显示时又有过渡");
            scene.Visibility = Visibility.Hidden;
            Pump(30);
            Check(Near(arms[0].Angle, HomeAngle) && !arms[0].HasAnimatedProperties, "过渡中被隐藏直接落到目标");
            scene.Visibility = Visibility.Visible;
            Pump(30);

            // 7. Lift 跟到位反馈：未知（命令发了、到位信号还没亮）停在行程中间并高亮，升到位后摆臂抬高、工艺位液柱长一个行程
            state = State(arms: 2);
            state.Arms[0].Reach = 1;
            devices.Update(state.ToDto());
            Pump(450);
            var firstPipe = Pipes(arms[0])[0];
            double lowered = firstPipe.StreamLength;
            double pivotLowered = Pivot(arms[0], plate).Y;
            state = State(arms: 2);
            state.Arms[0].Reach = 1;
            state.Arms[0].Lift.Position = CylinderPosition.Unknown;
            devices.Update(state.ToDto());
            Pump(550);
            Check(lifts[0].IsUnknown && !lifts[0].IsRaised && Near(firstPipe.StreamLength - lowered, LiftStroke / 2),
                "Lift 未知：停在行程中间，液柱长半个行程");
            state.Arms[0].Lift.Position = CylinderPosition.Opened;
            devices.Update(state.ToDto());
            Check(lifts[0].IsRaised && !lifts[0].IsUnknown, "升到位信号来了：Lift 往上位走");
            Pump(550);
            Check(Pivot(arms[0], plate).Y > pivotLowered && Near(firstPipe.StreamLength - lowered, LiftStroke),
                "Lift 升到位：摆臂抬高，液柱长一个行程");
            Check(Near(Landing(firstPipe, plate).Y, diskTop), "抬高后液柱仍落在盘面");

            // 8. 门、Bowl 跟到位反馈（未知停中间）、旋转跟实际转速的正负、摆臂在动时高亮；盘上的片跟 Wafer
            state = State(arms: 2);
            state.Arms[0].IsMoving = true;
            state.Door!.Position = CylinderPosition.Unknown;
            state.Bowl.Position = CylinderPosition.Opened;
            state.Spin.Speed = -100;
            devices.Update(state.ToDto());
            Check(door.IsUnknown && !door.IsOpen && bowl.IsRaised && !bowl.IsUnknown, "门未知（画在中间、高亮）、Bowl 升到位");
            Check(disk.RotationSpeed > 1 && !disk.RotateClockwise, "在转就按显示转速转，转向看实际转速的正负");
            Check(arms[0].IsMoving && !arms[1].IsMoving, "摆臂在动时高亮");
            devices.Update(State(arms: 2).ToDto());
            Check(disk.RotationSpeed == 0 && !door.IsOpen && !door.IsUnknown && !bowl.IsRaised, "停了、关到位了跟着变");
            var wafer = new WaferModel { LpSlot = "LP1-03", State = "Process" };
            scene.Wafer = wafer;
            Check(ReferenceEquals(disk.Data, wafer), "盘上的片跟 Wafer 走");

            // 8b. Bowl 和旋转盘的高低：降下时上沿比盘低（露出盘面好取放片），升起来才比盘高；盘下面主轴撑到底座面
            Pump(300);
            Rect3D Bounds(ModelVisual3D visual) => visual.TransformToAncestor(plate).TransformBounds(visual.Content.Bounds);
            double diskBottom = disk.TransformToAncestor(plate).Transform(new Point3D()).Y;
            Check(Bounds(bowl).Y + Bounds(bowl).SizeY < diskBottom, "Bowl 降下：上沿比旋转盘低，露出盘面");
            Check(Near(Bounds(disk).Y, 0), "旋转盘下面的主轴撑到底座面，不悬空");
            state = State(arms: 2);
            state.Bowl.Position = CylinderPosition.Opened;
            devices.Update(state.ToDto());
            Pump(300);
            Check(Bounds(bowl).Y + Bounds(bowl).SizeY > diskTop && Near(Bounds(bowl).Y, 0), "Bowl 升起：上沿比盘面高、底边还在底座面上");
            devices.Update(State(arms: 2).ToDto());
            Pump(300);

            // 9. 视角工具栏在图下面，只有默认视角、俯视（左转、右转、放大用户说不要；不叫"复位"，免得跟设备复位重名）；俯视改视角和视野，默认视角回来
            var buttons = Descendants<Button>(scene).ToList();
            Check(buttons.Select(Text).SequenceEqual(["默认视角", "俯视"]), "视角工具栏只有默认视角、俯视两个按钮，字按语言包");
            Check(buttons.All(button => Grid.GetRow(Toolbar(scene, button)) == 1), "工具栏在图下面一行，不浮在图上");
            var camera = (OrthographicCamera)scene.FindName("Camera");
            double defaultWidth = camera.Width;
            var defaultLook = camera.LookDirection;
            buttons[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(camera.LookDirection.Y < defaultLook.Y && camera.Width > defaultWidth, "俯视：相机几乎垂直朝下、视野放宽看全");
            buttons[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(Near(camera.Width, defaultWidth) && (camera.LookDirection - defaultLook).Length < 1e-6, "默认视角：视角和视野回到默认");
            Check(DeviceActionText.Of("Move") == "移动" && DeviceActionText.Of("Reset") == "复位" && DeviceActionText.Of("Up") == "升"
                && DeviceActionText.Of("ValveOn") == "开阀" && DeviceActionText.Of("MoveTo") == "MoveTo",
                "报错里的动作名（ChamberDeviceAction）按语言包换成叫法，不认识的原样给");
            Check(Enum.GetNames<ChamberDeviceAction>().All(action => DeviceActionText.Of(action) != action),
                "每个设备动作在语言包里都有叫法");

            // 10. 设备组成变了重搭：一条摆臂、没有门
            devices.Update(State(arms: 1, door: false).ToDto());
            Pump(30);
            Check(devices.Revision == 2 && plate.Attachments.Count == 5 && !plate.Attachments.OfType<DoorVisual3D>().Any()
                && plate.Attachments.OfType<ArmVisual3D>().Count() == 1, "组成变了重搭：sc 没配门就不画门，只剩一条摆臂");
            Check(!plate.Attachments.Contains(arms[1]) && !BindingOperations.IsDataBound(arms[1], HardwareVisual3D.IsMovingProperty),
                "拆掉的摆臂解了绑定");

            // 11. 多于两条摆臂：只画前两条，日志提示一次
            while (ClientLog.Reader.TryRead(out _))
            {
            }

            devices.Update(State(arms: 3).ToDto());
            Pump(30);
            Check(plate.Attachments.OfType<ArmVisual3D>().Count() == 2, "多于两条摆臂只画两条");
            Check(ClientLog.Reader.TryRead(out var warning) && warning.Message.Contains("Chamber1"), "多配的摆臂在日志里提示");

            // 12. 出图（工艺位、出液、Bowl 升起）
            state = State(arms: 2);
            state.Arms[0].Reach = 1;
            state.Arms[0].Nozzles[0].IsOn = true;
            state.Arms[1].Nozzles[1].IsOn = true;
            state.Bowl.Position = CylinderPosition.Opened;
            devices.Update(state.ToDto());
            Pump(650);
            if (args.Length > 0)
            {
                var bitmap = new RenderTargetBitmap(900, 600, 96, 96, PixelFormats.Pbgra32);
                scene.Measure(new Size(900, 600));
                scene.Arrange(new Rect(0, 0, 900, 600));
                scene.UpdateLayout();
                bitmap.Render(scene);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string output = Path.GetFullPath(args[0]);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var file = File.Create(output);
                encoder.Save(file);
                Console.WriteLine($"Preview: {output}");
            }

            // 13. 解绑：Devices 置空只剩旋转盘，门和 Bowl 不再绑旧模型
            scene.Devices = null;
            Check(plate.Attachments.Count == 1 && !BindingOperations.IsDataBound(door, DoorVisual3D.IsOpenProperty)
                && !BindingOperations.IsDataBound(door, DoorVisual3D.IsUnknownProperty)
                && !BindingOperations.IsDataBound(bowl, BowlVisual3D.IsRaisedProperty), "Devices 置空：只剩旋转盘，门和 Bowl 解绑");

            host.RootVisual = null;
            app.Shutdown();
            Console.WriteLine($"PASS: {_checks} chamber scene checks (devices picked from the devices push by role, part action labels, process/home alignment, stream landing, "
                + "0.2 s transition with retarget and hidden settle, lift/door/bowl follow feedback with unknown mid-stroke, spin direction, "
                + "default-view / top-view toolbar below the view, rebuild, arm limit, unbind)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static SceneState State(int arms, bool door = true)
    {
        var state = new SceneState { Door = door ? new CylinderState() : null };
        for (int i = 1; i <= arms; i++)
        {
            state.Arms.Add(new ArmState($"Arm{i}"));
        }

        return state;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var item in Descendants<T>(child))
            {
                yield return item;
            }
        }
    }

    /// <summary>按钮里的字（图标 + 文字）。</summary>
    private static string Text(Button button)
    {
        return Descendants<TextBlock>(button).Select(text => text.Text).FirstOrDefault(text => !string.IsNullOrEmpty(text)) ?? string.Empty;
    }

    /// <summary>按钮所在的那一块（ChamberScene 根 Grid 下的直接子元素）。</summary>
    private static UIElement Toolbar(ChamberScene scene, DependencyObject element)
    {
        var root = (DependencyObject)scene.Content;
        var current = element;
        while (VisualTreeHelper.GetParent(current) is DependencyObject parent && !ReferenceEquals(parent, root))
        {
            current = parent;
        }

        return (UIElement)current;
    }

    private static List<FluidPipeVisual3D> Pipes(ArmVisual3D arm)
    {
        return arm.Attachments.OfType<FluidPipeVisual3D>().ToList();
    }

    private static Point3D Pivot(ArmVisual3D arm, Visual3D plate)
    {
        return arm.TransformToAncestor(plate).Transform(new Point3D());
    }

    private static Point3D Nozzle(FluidPipeVisual3D pipe, Visual3D plate)
    {
        return pipe.TransformToAncestor(plate).Transform(new Point3D(pipe.Length, -FluidPipeVisual3D.OutletDrop, 0));
    }

    private static Point3D Landing(FluidPipeVisual3D pipe, Visual3D plate)
    {
        return pipe.TransformToAncestor(plate)
            .Transform(new Point3D(pipe.Length, -FluidPipeVisual3D.OutletDrop - pipe.StreamLength, 0));
    }

    private static Point3D Middle(Point3D a, Point3D b)
    {
        return new Point3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
    }

    private static double Horizontal(Point3D a, Point3D b)
    {
        return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
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

    private static bool Near(double a, double b)
    {
        return Math.Abs(a - b) < 1e-6;
    }

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
/// 冒烟用的设备状态：一项项改，ToDto() 换成后端推的设备表（轴表、气缸表、喷嘴表，门 / Bowl / Lift 标好角色），跟 sc 里 Chamber1 的结构一样：
/// Door、Bowl1、SpinMotor、ArmN（下面 Lift、Nozzle_DIW、Nozzle_SC1）。
/// </summary>
internal sealed class SceneState
{
    public const string Module = "Chamber1";

    public CylinderState? Door { get; set; }

    public CylinderState Bowl { get; } = new();

    public SpinState Spin { get; } = new();

    public List<ArmState> Arms { get; } = [];

    public ChamberPartsDto ToDto()
    {
        var dto = new ChamberPartsDto { Module = Module };
        if (Door is not null)
        {
            dto.Cylinders.Add(Cylinder($"{Module}.Door", Door, ChamberCylinderRole.Door, string.Empty));
        }

        dto.Cylinders.Add(Cylinder($"{Module}.Bowl1", Bowl, ChamberCylinderRole.Bowl, string.Empty));
        dto.Axes.Add(new ChamberAxisDto
        {
            Path = $"{Module}.SpinMotor",
            Kind = ChamberAxisKind.Spin,
            CurrentSpeed = Spin.Speed,
            IsSpinning = Spin.Speed != 0,
        });
        foreach (var arm in Arms)
        {
            string path = $"{Module}.{arm.Name}";
            dto.Axes.Add(new ChamberAxisDto
            {
                Path = path,
                Kind = ChamberAxisKind.Arm,
                Reach = arm.Reach,
                EdgeReach = arm.EdgeReach,
                IsBusy = arm.IsMoving,
            });
            dto.Cylinders.Add(Cylinder($"{path}.Lift", arm.Lift, ChamberCylinderRole.Lift, path));
            for (int i = 0; i < arm.Nozzles.Count; i++)
            {
                dto.Nozzles.Add(new ChamberNozzleDto
                {
                    Path = $"{path}.{(i == 0 ? "Nozzle_DIW" : "Nozzle_SC1")}",
                    Arm = path,
                    Chemical = i == 0 ? "DIW" : "SC1",
                    IsOn = arm.Nozzles[i].IsOn,
                });
            }
        }

        return dto;
    }

    private static ChamberCylinderDto Cylinder(string path, CylinderState cylinder, ChamberCylinderRole role, string arm)
    {
        return new ChamberCylinderDto
        {
            Path = path,
            Role = role,
            Arm = arm,
            Position = cylinder.Position,
        };
    }
}

internal sealed class CylinderState
{
    public CylinderPosition Position { get; set; } = CylinderPosition.Closed;
}

internal sealed class SpinState
{
    /// <summary>实际转速，0 = 停，正负是转向。</summary>
    public double Speed { get; set; }
}

internal sealed class ArmState(string name)
{
    public string Name { get; } = name;

    public double Reach { get; set; }

    public double EdgeReach { get; set; }

    public bool IsMoving { get; set; }

    public CylinderState Lift { get; } = new();

    public List<NozzleState> Nozzles { get; } = [new NozzleState(), new NozzleState()];
}

internal sealed class NozzleState
{
    public bool IsOn { get; set; }
}
