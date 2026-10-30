using System.Collections;
using System.Globalization;
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
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace FrontendControlsGuide;

internal static partial class Program
{
    private static readonly Dictionary<DependencyObject, Action> CaptureCleanup = [];
    private static void ExportControl(Type type, ControlInfo entry)
    {
        var presets = Presets(type);
        for (int i = 0; i < presets.Count; i++)
        {
            var (label, values) = presets[i];
            var instance = (DependencyObject)Activator.CreateInstance(type)!; Prepare(instance);
            foreach (var pair in values) Set(instance, pair.Key, pair.Value);
            int width = 640;
            int height = !entry.Is3D && type.Name is "InputTextBox" or "DateTimePicker" or "PickerBox" or "HoldButton" or "SignalLamp" or "AlarmBar" or "LogBar" or "ModuleChip" or "ModuleStateBadge" or "ModuleStatusBar" ? 130 : 360;
            var root = new Border { Background = (Brush)Application.Current.FindResource("DarkSurfaceBackground"), Padding = new Thickness(20),
                Width = width, Height = height };
            root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Application.Current.FindResource("DarkPrimaryText"));
            Viewport3D? viewport = null;
            if (instance is ModelVisual3D visual)
            {
                viewport = new Viewport3D();
                var lights = new Model3DGroup(); lights.Children.Add(new AmbientLight(Color.FromRgb(130, 130, 130)));
                lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -3, -2)));
                viewport.Children.Add(new ModelVisual3D { Content = lights }); viewport.Children.Add(visual);
                var bounds = visual.Content?.Bounds ?? Rect3D.Empty;
                foreach (var child in visual.Children.OfType<ModelVisual3D>()) if (child.Content is { } content) bounds.Union(content.Bounds);
                if (bounds.IsEmpty) bounds = new Rect3D(-2, 0, -2, 4, 4, 4);
                var target = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
                double size = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
                var offset = new Vector3D(-size * 1.6, size * 1.2, size * 2.2);
                viewport.Camera = new OrthographicCamera(target + offset, -offset, new Vector3D(0, 1, 0), size * 2.05);
                root.Child = viewport;
            }
            else
            {
                var control = (FrameworkElement)instance;
                if (!entry.Is3D && type.Name is not ("Robot" or "TrendChart" or "Foup" or "Buffer" or "ChamberInfoCard" or "BottomChamberInfoCard" or "LoadPortInfoCard" or "StationCard" or "ProgressOverlay" or "DispatchMap"))
                { control.HorizontalAlignment = HorizontalAlignment.Center; control.VerticalAlignment = VerticalAlignment.Center; }
                if (type.Name == "SignalTower") { control.Width = 70; control.Height = 270; }
                if (type.Name == "Wafer") { control.Width = 210; control.Height = 210; }
                root.Child = control;
            }
            using var host = new HwndSource(new HwndSourceParameters("Frontend documentation capture")
            { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = width, Height = height });
            host.RootVisual = root; root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); Pump(60);
            string image = type.Name + "-" + i + ".png";
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(_output, image))) encoder.Save(stream);
            string? meshFile = null;
            if (entry.Is3D)
            {
                var meshes = new List<MeshInfo>();
                if (instance is Visual3D v) ExportVisual(v, Matrix3D.Identity, meshes);
                else foreach (var vp in Descendants(instance).OfType<Viewport3D>()) foreach (var child in vp.Children) ExportVisual(child, Matrix3D.Identity, meshes);
                if (meshes.Count > 0)
                {
                    meshFile = type.Name + "-" + i + ".mesh.js";
                    File.WriteAllText(Path.Combine(_output, meshFile), "window.WPF_MESHES=window.WPF_MESHES||{};window.WPF_MESHES['" + type.Name + "-" + i + "']=" + JsonSerializer.Serialize(meshes, Json) + ";");
                }
            }
            entry.Variants.Add(new VariantInfo(label, values.ToDictionary(p => p.Key, p => Value(p.Value)), image, meshFile));
            root.Visibility = Visibility.Hidden; host.RootVisual = null;
            if (CaptureCleanup.Remove(instance, out var cleanup)) cleanup();
            if (instance is IDisposable disposable) disposable.Dispose();
        }
    }

    private static List<(string Label, Dictionary<string, object?> Values)> Presets(Type type)
    {
        var result = new List<(string, Dictionary<string, object?>)> { ("基础示例", []) };
        var variants = type.Name switch
        {
            "SignalLamp" => new[] { ("触发", "IsActive", (object?)true), ("未触发", "IsActive", (object?)false), ("未知", "IsActive", null), ("报警位触发", "IsAlarm", (object?)true) },
            "InputTextBox" => new[] { ("整数", "DataType", (object?)InputDataType.Integer), ("错误状态", "HasError", (object?)true) },
            "ArmVisual3D" => new[] { ("Angle = 45°", "Angle", (object?)45d), ("Angle = 90°", "Angle", (object?)90d), ("加长至 3.2", "Length", (object?)3.2d) },
            "TankVisual3D" => new[] { ("液面 25%（示例输入）", "FillFraction", (object?)0.25d), ("液面 80%（示例输入）", "FillFraction", (object?)0.8d), ("报警液面", "IsAlarm", (object?)true) },
            "BowlVisual3D" => new[] { ("升到位", "IsRaised", (object?)true), ("位置未知", "IsUnknown", (object?)true), ("外观高度 2", "HeightLevel", (object?)2) },
            "LiftVisual3D" => new[] { ("升到位", "IsRaised", (object?)true), ("位置未知", "IsUnknown", (object?)true) },
            "DoorVisual3D" => new[] { ("开到位", "IsOpen", (object?)true), ("位置未知", "IsUnknown", (object?)true) },
            "FluidRouteVisual3D" or "FluidPipeVisual3D" or "FluidPipe" => new[] { ("出液显示", "IsFlowing", (object?)true), ("关闭动画", "IsAnimationEnabled", (object?)false) },
            "LoadPortVisual3D" or "LoadPortScene" => new[] { ("已对接", "IsDocked", (object?)true), ("门打开", "IsDoorOpen", (object?)true) },
            "CarrierVisual3D" or "BufferVisual3D" or "BufferView" or "Buffer" => new[] { ("8 个槽（示例）", "SlotCount", (object?)8) },
            "ModuleChip" => new[] { ("维护模式", "IsProduction", (object?)false), ("不区分生产维护", "HasMode", (object?)false) },
            "ModuleStateBadge" => new[] { ("紧凑模式", "IsCompact", (object?)true) },
            "Robot" or "Robot3D" => new[] { ("朝东（90°）", "Rotation", (object?)RobotDirection.East), ("蛙式机械手", "ArmType", (object?)RobotArmType.FrogLeg) },
            "SignalTower" => new[] { ("红灯", "IsRedOn", (object?)true), ("黄灯", "IsYellowOn", (object?)true) },
            _ => Array.Empty<(string, string, object?)>()
        };
        foreach (var v in variants) if (type.GetProperty(v.Item2)?.CanWrite == true) result.Add((v.Item1, new Dictionary<string, object?> { [v.Item2] = v.Item3 }));
        if (typeof(HardwareVisual3D).IsAssignableFrom(type))
        { result.Add(("选中高亮", new Dictionary<string, object?> { ["IsSelected"] = true })); result.Add(("故障显示", new Dictionary<string, object?> { ["IsError"] = true })); }
        return result;
    }

    private static void Prepare(DependencyObject d)
    {
        void Try(string name, object? value) { var p = d.GetType().GetProperty(name); if (p?.CanWrite == true && (value is null || p.PropertyType.IsInstanceOfType(value))) p.SetValue(d, value); }
        var wafer = new WaferModel { Slot = 1, LpSlot = "LP1-01", State = "Process", LoadPort = "LoadPort1" };
        var wafers = new[] { wafer, new WaferModel { Slot = 3, LpSlot = "LP1-03", State = "Completed" } };
        Try("Text", "Chamber1"); Try("Title", "Chamber1"); Try("StateText", "就绪"); Try("IsOnline", "已连接"); Try("State", "就绪");
        Try("StatusOn", true); Try("IsConnected", true); Try("Present", true); Try("Placed", true); Try("IsPresent", true); Try("IsPlaced", true);
        Try("SlotCount", 5); Try("Wafer", wafer); Try("Data", wafer); Try("Wafers", wafers); Try("IsDiskVisible", true);
        Try("Recipe", "示例配方"); Try("Step", "1 / 5"); Try("IsProduction", true);
        foreach (string name in new[] { "Tone", "StateTone" }) if (d.GetType().GetProperty(name) is { PropertyType.IsEnum: true } tone)
        { string? ready = Enum.GetNames(tone.PropertyType).FirstOrDefault(n => n is "Ready" or "Running"); if (ready is not null) tone.SetValue(d, Enum.Parse(tone.PropertyType, ready)); }
        switch (d)
        {
            case SignalLamp lamp: lamp.Text = "Tank1.H"; lamp.IsActive = true; break;
            case InputTextBox input: input.DataType = InputDataType.Decimal; input.Minimum = 0; input.Maximum = 100; input.Unit = "L/min"; input.Value = "25.5"; input.Width = 260; break;
            case DateTimePicker picker: picker.Value = new DateTime(2026, 10, 30, 8, 30, 0); picker.Hint = "开始时间"; picker.Width = 440; break;
            case PickerBox picker: picker.Value = "Recipe_A"; picker.PickerTitle = "选择配方"; picker.ItemsSource = new[] { new { Name = "Recipe_A" }, new { Name = "Recipe_B" } }; picker.Width = 260; break;
            case HoldButton button: button.Content = "按住点动 / 松开停止"; break;
            case TankVisual3D tank: tank.FillFraction = 0.47; break;
            case FluidRouteVisual3D route: route.Path = new Point3DCollection { new(-1.5, 0, 0), new(0, 0, 0), new(0, 1.2, 0), new(1.5, 1.2, 0) }; break;
            case FluidPipe pipe: pipe.Points = new PointCollection { new(10, 120), new(170, 120), new(170, 50), new(340, 50) }; pipe.Width = 400; pipe.Height = 200; break;
            case TankScene scene:
                var model = new TankModel(); model.Update(new TankDto { Level = 3, LL = new DiSensorDto { Name = "LL", IsActive = true }, L = new DiSensorDto { Name = "L", IsActive = true }, H = new DiSensorDto { Name = "H", IsActive = false }, HH = new DiSensorDto { Name = "HH", IsActive = false } }); scene.Tank = model; break;
            case RobotViewBase robot: robot.Arms = new[] { new RobotArmModel { Arm = 1, Extension = 0.8, Wafer = wafer }, new RobotArmModel { Arm = 2, Extension = 0, Heading = 180 } }; break;
            case RobotVisual3D robot:
                var source = new Robot { Arms = new[] { new RobotArmModel { Arm = 1, Extension = 0.8, Wafer = wafer }, new RobotArmModel { Arm = 2, Heading = 180 } } };
                robot.SetPose(RobotArmType.Linear, 0, 0, source.ArmVisuals); break;
            case ProgressOverlay overlay:
                var progress = new ProgressState(); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                overlay.ShowDelay = 0; overlay.Text = "文档演示：正在处理"; overlay.State = progress;
                _ = progress.RunAsync((reporter, token) => { reporter.Report(65); return completion.Task; });
                CaptureCleanup.Add(overlay, () => completion.TrySetResult()); break;
            case ChamberScene chamber:
                var devices = new ChamberDeviceDataModel(); devices.Update(ChamberData()); chamber.Devices = devices; chamber.Wafer = wafer; break;
            case xyz._35021.Client.Presentation.Controls.ChamberView chamber: chamber.Snapshot = ChamberData(); chamber.Wafer = wafer; break;
            case StationCard station: station.DataContext = new RobotStationModel { Name = "Chamber1", Number = 3 }; break;
            case TrendChart chart:
                DateTime start = new(2026, 10, 30, 8, 0, 0); var series = new TrendSeries("Chamber1.Temperature", "℃", false);
                series.SetData(Enumerable.Range(0, 60).Select(i => start.AddMinutes(i).ToOADate()), Enumerable.Range(0, 60).Select(i => 25 + Math.Sin(i * 0.2) * 3));
                chart.Series = new[] { series }; chart.RangeStart = start; chart.RangeEnd = start.AddMinutes(59); break;
            case ModuleStatusBar bar: bar.DataContext = new { Modules = new[] { new { Name = "Chamber1", StateTone = ModuleStateTone.Ready, IsProduction = true, HasMode = true, StateText = "就绪" }, new { Name = "IPA", StateTone = ModuleStateTone.Ready, IsProduction = true, HasMode = false, StateText = "就绪" } } }; break;
        }
    }
    private static ChamberDeviceDataDto ChamberData() => new()
    {
        Module = "Chamber1", Door = new ChamberCylinderDto { Path = "Chamber.Chamber1.Door", Position = CylinderPosition.Closed },
        Bowls = [new ChamberCylinderDto { Path = "Chamber.Chamber1.Bowl1", Position = CylinderPosition.Closed }],
        Spin = new ChamberAxisDto { Path = "Chamber.Chamber1.SpinMotor", HasPlcData = true },
        Arms = [new ChamberArmDto { Path = "Chamber.Chamber1.Arm1", HasPlcData = true, Reach = 0.7,
            Lift = new ChamberCylinderDto { Path = "Chamber.Chamber1.Arm1.Lift", Position = CylinderPosition.Closed },
            Nozzles = [new ChamberNozzleDto { Path = "Chamber.Chamber1.Arm1.Nozzle_DIW", Chemical = "DIW" }] }]
    };
    private static void Set(DependencyObject d, string name, object? value) => d.GetType().GetProperty(name)!.SetValue(d, value);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject d)
    {
        yield return d;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(d, i))) yield return child;
    }
    private static void ExportVisual(Visual3D visual, Matrix3D parent, List<MeshInfo> meshes)
    {
        var matrix = visual.Transform?.Value ?? Matrix3D.Identity; matrix.Append(parent);
        if (visual is ModelVisual3D { Content: { } model }) ExportModel(model, matrix, meshes);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++) if (VisualTreeHelper.GetChild(visual, i) is Visual3D child) ExportVisual(child, matrix, meshes);
    }
    private static void ExportModel(Model3D model, Matrix3D parent, List<MeshInfo> meshes)
    {
        var matrix = model.Transform?.Value ?? Matrix3D.Identity; matrix.Append(parent);
        if (model is Model3DGroup group) foreach (var child in group.Children) ExportModel(child, matrix, meshes);
        if (model is not GeometryModel3D { Geometry: MeshGeometry3D mesh, Material: { } material } || mesh.TriangleIndices.Count == 0) return;
        var positions = new float[mesh.Positions.Count * 3];
        for (int i = 0; i < mesh.Positions.Count; i++) { var p = matrix.Transform(mesh.Positions[i]); positions[i * 3] = Round(p.X); positions[i * 3 + 1] = Round(p.Y); positions[i * 3 + 2] = Round(p.Z); }
        var color = MaterialColor(material); meshes.Add(new MeshInfo(positions, mesh.TriangleIndices.ToArray(), color));
    }
    private static float Round(double value) => (float)Math.Round(value, 5);
    private static float[] MaterialColor(Material m)
    {
        var diffuse = m as DiffuseMaterial ?? (m as MaterialGroup)?.Children.OfType<DiffuseMaterial>().FirstOrDefault();
        var brush = diffuse?.Brush; var color = (brush as SolidColorBrush)?.Color ?? Color.FromRgb(125, 142, 156);
        return [color.R / 255f, color.G / 255f, color.B / 255f, (float)(color.A / 255d * (brush?.Opacity ?? 1))];
    }
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
