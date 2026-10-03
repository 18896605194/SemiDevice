using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using xyz.Client.Common.Log;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

// 腔体三维图冒烟：按部件组成搭建、摆臂工艺位对盘心 / Home 对接液杯、液柱落点、0.2 s 过渡（中途换目标不跳、隐藏时直接落位）、
// Lift 升降带动摆臂和液柱、门 / Bowl / 旋转跟状态、组成变化重搭、多于两条摆臂只画两条、解绑。不连后端，状态直接喂显示模型。
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
            // 跟壳的 App.xaml 一样并进中文语言包：按钮字、日志里的提示才是按模板拼好的句子。
            foreach (string name in new[] { "Styles/DarkColors", "Styles/FontSize", "Localization/Strings.zh-CN" })
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

            // 1. 还没有部件组成：只画旋转盘
            Check(plate.Attachments.Count == 1 && plate.Attachments[0] is DiskVisual3D, "没收到部件组成时只画旋转盘");
            var disk = (DiskVisual3D)plate.Attachments[0];

            // 2. 按部件组成搭：门、Bowl、旋转盘，两条摆臂各一套 Lift / 摆臂（两路喷嘴管）/ 接液杯；按钮组照 sc 路径
            var parts = new ChamberPartsModel();
            scene.Parts = parts;
            var dto = Dto(arms: 2);
            dto.Arms[0].Reach = 1;
            dto.Arms[1].Reach = 1;
            parts.Update(dto);
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
            Check(parts.Revision == 1 && parts.Groups.Select(group => group.Title).SequenceEqual(
                ["Door", "Bowl1", "SpinMotor", "Arm1", "Arm1.Lift", "Arm1.Nozzle_DIW", "Arm1.Nozzle_SC1",
                 "Arm2", "Arm2.Lift", "Arm2.Nozzle_DIW", "Arm2.Nozzle_SC1"]), "按钮组照 sc 路径（去掉腔体名）、按部件顺序");
            Check(parts.Groups[0].Actions.Select(action => action.Action).SequenceEqual([ChamberPartAction.Open, ChamberPartAction.Close])
                && parts.Groups[2].Actions.Select(action => action.Action).SequenceEqual([ChamberPartAction.Start, ChamberPartAction.Stop])
                && parts.Groups[3].Actions.Select(action => action.Action).SequenceEqual([ChamberPartAction.Home, ChamberPartAction.Center])
                && parts.Groups[5].Actions.Select(action => action.Action).SequenceEqual([ChamberPartAction.On, ChamberPartAction.Off])
                && parts.Groups[4].Actions[0].Path == "Chamber1.Arm1.Lift", "每组按钮按部件种类给，带全路径");
            Check(parts.Groups[0].Actions[0].Text == "开" && parts.Groups[1].Actions[0].Text == "升"
                && parts.Groups[3].Actions[1].Text == "工艺位" && parts.Groups[5].Actions[0].Text == "出液"
                && parts.Groups[4].Actions[0].Part == "Arm1.Lift", "按钮字按语言包取（门开 / Bowl 升 / 工艺位 / 出液），失败提示用 sc 路径");

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
            dto = Dto(arms: 2);
            dto.Arms[0].Nozzles[0].IsOn = true;
            parts.Update(dto);
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
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 1;
            parts.Update(dto);
            Pump(100);
            double midway = arms[0].Angle;
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 0.5;
            parts.Update(dto);
            Check(Near(arms[0].Angle, midway), "中途换目标不跳");
            Pump(450);
            Check(Near(arms[0].Angle, HomeAngle + 0.5 * (processAngle - HomeAngle)) && !arms[0].HasAnimatedProperties, "Reach 0.5 落在 Home 和工艺位中间");

            // 5b. 示教过边缘：Home → 边缘 → 中心分两段；轴在 Edge 时喷嘴正好在盘边（靠 Home 那侧），只改边缘也会重新摆
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 0.5;
            dto.Arms[0].EdgeReach = 0.5;
            parts.Update(dto);
            Pump(450);
            double edgeAngle = arms[0].Angle;
            var edgeMiddle = Middle(Nozzle(Pipes(arms[0])[0], plate), Nozzle(Pipes(arms[0])[1], plate));
            Check(!Near(edgeAngle, HomeAngle + 0.5 * (processAngle - HomeAngle)), "只改边缘位置也重新摆");
            Check(Math.Abs(Horizontal(edgeMiddle, center) - disk.Radius) < 1e-6, "轴在 Edge：两路喷嘴中点正好在盘边");
            Check(edgeAngle < HomeAngle && edgeAngle > processAngle, "边缘在 Home 和工艺位之间（第一个边缘）");
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 0.25;
            dto.Arms[0].EdgeReach = 0.5;
            parts.Update(dto);
            Pump(450);
            Check(Near(arms[0].Angle, (HomeAngle + edgeAngle) / 2), "Home 到边缘之间按轴位置线性");
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 0.75;
            dto.Arms[0].EdgeReach = 0.5;
            parts.Update(dto);
            Pump(450);
            Check(Near(arms[0].Angle, (edgeAngle + processAngle) / 2), "边缘到中心之间按轴位置线性");
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 1;
            dto.Arms[0].EdgeReach = 0.5;
            parts.Update(dto);
            Pump(450);
            var centerMiddle = Middle(Nozzle(Pipes(arms[0])[0], plate), Nozzle(Pipes(arms[0])[1], plate));
            Check(Near(arms[0].Angle, processAngle) && Horizontal(centerMiddle, center) < 1e-6, "轴在 Center 仍正对盘心");
            parts.Update(Dto(arms: 2));
            Pump(450);

            // 6. 页面隐藏：收到新位置直接落位；过渡中被隐藏也直接落到目标
            scene.Visibility = Visibility.Hidden;
            Pump(30);
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 1;
            parts.Update(dto);
            Check(Near(arms[0].Angle, processAngle) && !arms[0].HasAnimatedProperties, "隐藏时直接落位、不起动画");
            scene.Visibility = Visibility.Visible;
            Pump(30);
            parts.Update(Dto(arms: 2));
            Pump(100);
            Check(arms[0].HasAnimatedProperties, "显示时又有过渡");
            scene.Visibility = Visibility.Hidden;
            Pump(30);
            Check(Near(arms[0].Angle, HomeAngle) && !arms[0].HasAnimatedProperties, "过渡中被隐藏直接落到目标");
            scene.Visibility = Visibility.Visible;
            Pump(30);

            // 7. Lift 升：摆臂跟着抬高，工艺位液柱跟着变长一个行程（管子局部单位）
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 1;
            parts.Update(dto);
            Pump(450);
            var firstPipe = Pipes(arms[0])[0];
            double lowered = firstPipe.StreamLength;
            double pivotLowered = Pivot(arms[0], plate).Y;
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 1;
            var lift = dto.Arms[0].Lift;
            if (lift is null)
            {
                throw new InvalidOperationException("FAIL: 冒烟数据里应有 Lift");
            }

            lift.IsOpen = true;
            lift.IsMoving = true;
            parts.Update(dto);
            Check(lifts[0].IsRaised && lifts[0].IsMoving, "Lift 跟指令侧升起、在走时高亮");
            Pump(550);
            Check(Pivot(arms[0], plate).Y > pivotLowered && Near(firstPipe.StreamLength - lowered, LiftStroke),
                "Lift 升到位：摆臂抬高，液柱长一个行程");
            Check(Near(Landing(firstPipe, plate).Y, diskTop), "抬高后液柱仍落在盘面");

            // 8. 门、Bowl、旋转、动作高亮跟状态；盘上的片跟 Wafer
            dto = Dto(arms: 2);
            dto.Arms[0].IsMoving = true;
            SetCylinder(dto.Door, open: true, moving: true);
            SetCylinder(dto.Bowl, open: true, moving: false);
            var spin = dto.Spin;
            if (spin is null)
            {
                throw new InvalidOperationException("FAIL: 冒烟数据里应有旋转电机");
            }

            spin.IsSpinning = true;
            spin.IsClockwise = false;
            parts.Update(dto);
            Check(door.IsOpen && door.IsMoving && bowl.IsRaised && !bowl.IsMoving, "门、Bowl 跟指令侧，在走时高亮");
            Check(disk.RotationSpeed > 1 && !disk.RotateClockwise, "在转就按显示转速转，转向跟实际");
            Check(arms[0].IsMoving && !arms[1].IsMoving, "摆臂在动时高亮");
            parts.Update(Dto(arms: 2));
            Check(disk.RotationSpeed == 0 && !door.IsOpen && !bowl.IsRaised, "停了、关了跟着变");
            var wafer = new WaferModel { LpSlot = "LP1-03", State = "Process" };
            scene.Wafer = wafer;
            Check(ReferenceEquals(disk.Data, wafer), "盘上的片跟 Wafer 走");

            // 8b. Bowl 和旋转盘的高低：降下时上沿比盘低（露出盘面好取放片），升起来才比盘高；盘下面主轴撑到底座面
            Pump(300);
            Rect3D Bounds(ModelVisual3D visual) => visual.TransformToAncestor(plate).TransformBounds(visual.Content.Bounds);
            double diskBottom = disk.TransformToAncestor(plate).Transform(new Point3D()).Y;
            Check(Bounds(bowl).Y + Bounds(bowl).SizeY < diskBottom, "Bowl 降下：上沿比旋转盘低，露出盘面");
            Check(Near(Bounds(disk).Y, 0), "旋转盘下面的主轴撑到底座面，不悬空");
            dto = Dto(arms: 2);
            SetCylinder(dto.Bowl, open: true, moving: false);
            parts.Update(dto);
            Pump(300);
            Check(Bounds(bowl).Y + Bounds(bowl).SizeY > diskTop && Near(Bounds(bowl).Y, 0), "Bowl 升起：上沿比盘面高、底边还在底座面上");
            parts.Update(Dto(arms: 2));
            Pump(300);

            // 9. 部件组成变了重搭：一条摆臂、没有门
            parts.Update(Dto(arms: 1, door: false));
            Pump(30);
            Check(parts.Revision == 2 && plate.Attachments.Count == 5 && !plate.Attachments.OfType<DoorVisual3D>().Any()
                && plate.Attachments.OfType<ArmVisual3D>().Count() == 1, "组成变了重搭：sc 没配门就不画门，只剩一条摆臂");
            Check(!plate.Attachments.Contains(arms[1]) && !BindingOperations.IsDataBound(arms[1], HardwareVisual3D.IsMovingProperty),
                "拆掉的摆臂解了绑定");
            Check(parts.Groups.All(group => group.Title != "Door"), "没有门就没有门的按钮");

            // 10. 多于两条摆臂：只画前两条，日志提示一次
            while (ClientLog.Reader.TryRead(out _))
            {
            }

            parts.Update(Dto(arms: 3));
            Pump(30);
            Check(plate.Attachments.OfType<ArmVisual3D>().Count() == 2, "多于两条摆臂只画两条");
            Check(ClientLog.Reader.TryRead(out var warning) && warning.Message.Contains("Chamber1"), "多配的摆臂在日志里提示");

            // 11. 出图（工艺位、出液、Bowl 升起）
            dto = Dto(arms: 2);
            dto.Arms[0].Reach = 1;
            dto.Arms[0].Nozzles[0].IsOn = true;
            dto.Arms[1].Nozzles[1].IsOn = true;
            SetCylinder(dto.Bowl, open: true, moving: false);
            parts.Update(dto);
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

            // 12. 解绑：Parts 置空只剩旋转盘，门和 Bowl 不再绑旧模型
            scene.Parts = null;
            Check(plate.Attachments.Count == 1 && !BindingOperations.IsDataBound(door, DoorVisual3D.IsOpenProperty)
                && !BindingOperations.IsDataBound(bowl, BowlVisual3D.IsRaisedProperty), "Parts 置空：只剩旋转盘，门和 Bowl 解绑");

            host.RootVisual = null;
            app.Shutdown();
            Console.WriteLine($"PASS: {_checks} chamber scene checks (assembly from parts, process/home alignment, stream landing, "
                + "0.2 s transition with retarget and hidden settle, lift, door/bowl/spin/highlight bindings, rebuild, arm limit, unbind)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static ChamberPartsDto Dto(int arms, bool door = true)
    {
        var dto = new ChamberPartsDto
        {
            Name = "Chamber1",
            Bowl = new ChamberCylinderDto { Name = "Bowl1", Path = "Chamber1.Bowl1" },
            Spin = new ChamberSpinDto { Name = "SpinMotor", Path = "Chamber1.SpinMotor", IsClockwise = true },
        };
        if (door)
        {
            dto.Door = new ChamberCylinderDto { Name = "Door", Path = "Chamber1.Door" };
        }

        for (int i = 1; i <= arms; i++)
        {
            dto.Arms.Add(new ChamberArmDto
            {
                Name = $"Arm{i}",
                Path = $"Chamber1.Arm{i}",
                Lift = new ChamberCylinderDto { Name = "Lift", Path = $"Chamber1.Arm{i}.Lift" },
                Nozzles =
                [
                    new ChamberNozzleDto { Name = "Nozzle_DIW", Path = $"Chamber1.Arm{i}.Nozzle_DIW", Chemical = "DIW" },
                    new ChamberNozzleDto { Name = "Nozzle_SC1", Path = $"Chamber1.Arm{i}.Nozzle_SC1", Chemical = "SC1" },
                ],
            });
        }

        return dto;
    }

    private static void SetCylinder(ChamberCylinderDto? cylinder, bool open, bool moving)
    {
        if (cylinder is null)
        {
            throw new InvalidOperationException("FAIL: 冒烟数据里应有这个气缸");
        }

        cylinder.IsOpen = open;
        cylinder.IsMoving = moving;
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
