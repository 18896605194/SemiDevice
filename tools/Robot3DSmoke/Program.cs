using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

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
            { BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark, PrimaryColor = MaterialDesignColors.PrimaryColor.Blue, SecondaryColor = MaterialDesignColors.SecondaryColor.Lime });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml") });
            foreach (string name in new[] { "Styles/Color", "Styles/DarkColors", "Styles/FontSize", "Styles/BorderStyles", "Styles/ButtonStyles", "Localization/Strings.zh-CN" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/xyz.Client.Presentation;component/{name}.xaml", UriKind.Relative) });

            var root = (Grid)Application.LoadComponent(new Uri("/Robot3DSmoke;component/Scene.xaml", UriKind.Relative));
            var flat = (Robot)root.FindName("Flat");
            var solid = (Robot3D)root.FindName("Solid");
            var bare = (Robot3D)root.FindName("Bare");
            var model = new Feedback
            {
                Arms = [new RobotArmModel { Arm = 1, Extension = 1, Wafer = new WaferModel { Slot = 1, State = WaferStates.Idle, LpSlot = "LP1-01" } },
                    new RobotArmModel { Arm = 2, Wafer = new WaferModel { Slot = 2, State = "Completed", LpSlot = "LP1-02" } }],
            };
            root.DataContext = model;
            using var host = new HwndSource(new HwndSourceParameters("Robot3D smoke")
            { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = 800, Height = 400 });
            host.RootVisual = root;
            Layout(root);
            Pump(120);
            var body = (RobotVisual3D)solid.FindName("Body");
            var viewport = (Viewport3D)solid.FindName("Viewport");
            var camera = (OrthographicCamera)solid.FindName("Camera");
            var disks = Descendants<DiskVisual3D>(body).ToArray();
            Check(bare.ArmVisuals.Count == 2 && Descendants<DiskVisual3D>((RobotVisual3D)bare.FindName("Body")).Count() == 2,
                "bare XAML tag creates a complete robot");
            Check(ReferenceEquals(flat.DataContext, model) && ReferenceEquals(solid.DataContext, model), "inherits the caller DataContext");
            foreach (var property in new[] { RobotViewBase.ArmTypeProperty, RobotViewBase.ArmCountProperty, RobotViewBase.ArmsProperty,
                         RobotViewBase.RotationProperty, RobotViewBase.TravelProperty, RobotViewBase.ExtendedReachProperty, RobotViewBase.IsAnimationEnabledProperty })
                Check(BindingOperations.IsDataBound(flat, property) && BindingOperations.IsDataBound(solid, property), "same bound property: " + property.Name);
            Check(ReferenceEquals(disks[0].Data, model.Arms[0].Wafer) && ReferenceEquals(disks[1].Data, model.Arms[1].Wafer), "framework disks share the original wafer objects");
            Check(Descendants<Wafer>(body).Count() == 2, "disk faces use real Wafer controls");
            EqualFrames(flat, solid);
            CheckMeshes(body);
            int linearParts = Meshes(body).Count();
            var meshes = Meshes(body).Select(m => m.Geometry).ToHashSet();
            model.Travel = 20; model.Direction = RobotDirection.East;
            Pump(20); EqualFrames(flat, solid); CheckDiskPositions(solid, body);
            Check(meshes.SetEquals(Meshes(body).Select(m => m.Geometry)), "position changes keep every mesh");
            model.Travel = 0; model.Direction = RobotDirection.North;
            model.Arms[1].Heading = 180; CheckDiskPositions(solid, body); model.Arms[1].Heading = 0;
            model.Arms[0].Wafer!.State = "Error"; model.Arms[0].Wafer!.LpSlot = "LP1-09"; Pump(20);
            Check(Descendants<Wafer>(body).Any(w => w.Data is WaferModel wafer && wafer.State == "Error" && wafer.LpSlot == "LP1-09"), "wafer color and label update in place");
            model.Arms[0].Wafer!.State = WaferStates.Idle; model.Arms[0].Wafer!.LpSlot = "LP1-01";
            if (args.Length > 0) Save(root, args[0], "robot3d-linear.png");

            model.ArmType = RobotArmType.FrogLeg; Pump(20); EqualFrames(flat, solid); CheckDiskPositions(solid, body); CheckMeshes(body);
            int frogParts = Meshes(body).Count();
            Check(frogParts > meshes.Count, "frog legs have their own links and joints");
            if (args.Length > 0) Save(root, args[0], "robot3d-frog.png");
            model.ArmType = RobotArmType.Linear; Pump(20);
            Check(Meshes(body).Count() == linearParts, "switching structures removes old parts");

            // 真实动作轨道，只在检查点补推当前帧，不改时间和动作顺序。
            model.Animate = true; Pump(100);
            model.Direction = RobotDirection.South;
            Pump(160); Tick(flat); Tick(solid);
            Check(solid.ArmVisuals[0].WaferTop > -150 && solid.ArmVisuals[0].WaferTop < -24 && Near(solid.ArmVisuals[0].Angle, 0), "retracts before turning");
            Pump(450); Tick(flat); Tick(solid);
            Check(solid.ArmVisuals[0].Angle > 0 && solid.ArmVisuals[0].Angle < 180 && Near(solid.ArmVisuals[0].WaferTop, -24), "turns with retracted arms");
            Pump(1000); Tick(flat); Tick(solid);
            Check(Near(solid.ArmVisuals[0].Angle, 180) && Near(solid.ArmVisuals[0].WaferTop, -150), "extends after arrival");
            Check(!Rendering(solid) && !Rendering(flat), "finished motion releases frame events");
            model.Arms[0].Extension = 0; model.Arms[1].Extension = 1;
            Pump(180); Tick(solid);
            Check(solid.ArmVisuals[0].WaferTop > -150 && Near(solid.ArmVisuals[1].WaferTop, -24), "arm swap retracts the old arm first");
            Pump(1000); Tick(solid);
            Check(Near(solid.ArmVisuals[0].WaferTop, -24) && Near(solid.ArmVisuals[1].WaferTop, -150), "other arm extends independently");

            solid.Visibility = Visibility.Hidden; model.Direction = RobotDirection.West;
            Check(Near(solid.ArmVisuals[1].Angle, 270) && !Rendering(solid), "hidden page settles immediately without rendering");
            solid.Visibility = Visibility.Visible; Pump(30);
            Check(Near(solid.ArmVisuals[1].Angle, 270), "visible page resumes at current state");
            model.Animate = false;
            solid.ShowViewToolbar = false; Layout(root);
            Check(((Border)solid.FindName("ViewToolbar")).Visibility == Visibility.Collapsed, "toolbar is optional");
            var directionBefore = model.Direction;
            solid.ShowTopView(); Check(camera.LookDirection.Y < -10 && Math.Abs(camera.LookDirection.Z) < 1, "top view changes the camera");
            solid.ResetView(); Check(Math.Abs(camera.LookDirection.Z) > 1 && model.Direction == directionBefore, "view operations do not change robot inputs");
            model.Reach = 260; model.Travel = -80; Layout(root);
            Check(camera.Width >= (model.Reach + Robot.WaferDiameter / 2 + Math.Abs(model.Travel)) * RobotVisual3D.UnitsPerPixel * 2, "camera fits larger reach and travel");

            model.ArmCount = 4;
            model.Arms.Add(new RobotArmModel { Arm = 3 }); model.Arms.Add(new RobotArmModel { Arm = 4 });
            Check(Descendants<DiskVisual3D>(body).Count() == 4, "one to four arms follow the same input");
            model.Arms.RemoveAt(1);
            Check(solid.ArmVisuals[1].Arm == 2 && solid.ArmVisuals[1].Wafer is null, "missing arm data uses an empty placeholder");
            model.ArmCount = 0; Check(solid.ArmCount == 1, "arm count is coerced like 2D");
            model.ArmCount = 2;
            var oldArms = model.Arms;
            model.Arms = [new RobotArmModel { Arm = 1 }, new RobotArmModel { Arm = 2 }];
            oldArms[0].Extension = 1; oldArms.Add(new RobotArmModel { Arm = 2, Extension = 1 });
            Check(solid.ArmVisuals.All(a => Near(a.WaferTop, -24)), "old collections and models are unbound");
            root.Children.Remove(solid); Pump(30);
            Check(!Rendering(solid), "removing the control stops rendering");
            model.Arms[0].Extension = 1;
            root.Children.Add(solid); Layout(root); Pump(30);
            Check(Near(solid.ArmVisuals[0].WaferTop, -model.Reach), "remount rebinds current arm data");
            Reject(solid, Robot.TravelProperty, double.NaN); Reject(solid, Robot.TravelProperty, double.PositiveInfinity);
            Reject(solid, Robot.ExtendedReachProperty, 20d); Reject(solid, Robot.ExtendedReachProperty, double.NaN);
            Reject(solid, Robot.RotationProperty, (RobotDirection)45);
            Console.WriteLine($"PASS: {_checks} Robot3D checks (XAML, 2D parity, both structures, motion, wafers, mesh reuse, binding, visibility, lifetime, camera, validation).");
            host.RootVisual = null; app.Shutdown(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void EqualFrames(Robot flat, Robot3D solid)
    {
        Check(flat.ArmVisuals.Count == solid.ArmVisuals.Count, "same displayed arm count");
        for (int i = 0; i < flat.ArmVisuals.Count; i++)
        {
            var a = flat.ArmVisuals[i]; var b = solid.ArmVisuals[i];
            Check(Near(a.Angle, b.Angle) && Near(a.WaferTop, b.WaferTop) && Near(a.StackOffset, b.StackOffset)
                && Near(a.ElbowLeftX, b.ElbowLeftX) && Near(a.ElbowLeftY, b.ElbowLeftY) && ReferenceEquals(a.Wafer, b.Wafer), "2D and 3D use identical frame data");
        }
    }

    private static void CheckDiskPositions(Robot3D control, RobotVisual3D body)
    {
        var disks = Descendants<DiskVisual3D>(body).ToArray();
        for (int i = 0; i < disks.Length; i++)
        {
            var frame = control.ArmVisuals[i]; double angle = frame.Angle * Math.PI / 180;
            double x = control.Travel + frame.StackOffset * Math.Cos(angle) - (frame.StackOffset + frame.WaferTop) * Math.Sin(angle);
            double z = frame.StackOffset * Math.Sin(angle) + (frame.StackOffset + frame.WaferTop) * Math.Cos(angle);
            Point3D point = disks[i].TransformToAncestor(body).Transform(new Point3D());
            Check(Near(point.X, x * RobotVisual3D.UnitsPerPixel) && Near(point.Z, z * RobotVisual3D.UnitsPerPixel), "3D wafer follows original translation and heading");
        }
    }

    private static void CheckMeshes(DependencyObject root)
    {
        bool valid = true;
        foreach (var model in Meshes(root))
        {
            var mesh = (MeshGeometry3D)model.Geometry;
            valid &= mesh.IsFrozen && mesh.Positions.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z));
            for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
            {
                int a = mesh.TriangleIndices[i], b = mesh.TriangleIndices[i + 1], c = mesh.TriangleIndices[i + 2];
                var normal = Vector3D.CrossProduct(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                valid &= normal.LengthSquared > 1e-12 && Vector3D.DotProduct(normal, mesh.Normals[a]) > 0;
            }
        }
        Check(valid, "static meshes are frozen, finite and have valid winding");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static IEnumerable<GeometryModel3D> Meshes(DependencyObject root)
    {
        foreach (var visual in Descendants<ModelVisual3D>(root))
            foreach (var model in Expand(visual.Content)) yield return model;
        static IEnumerable<GeometryModel3D> Expand(Model3D? model)
        {
            if (model is GeometryModel3D geometry) yield return geometry;
            if (model is Model3DGroup group)
                foreach (var child in group.Children)
                    foreach (var geometryChild in Expand(child)) yield return geometryChild;
        }
    }

    private static void Tick(RobotViewBase control) => typeof(RobotViewBase).GetMethod("OnRendering", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [null, EventArgs.Empty]);
    private static bool Rendering(RobotViewBase control) => (bool)typeof(RobotViewBase).GetField("_isRendering", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
    private static void Reject(DependencyObject obj, DependencyProperty property, object value)
    {
        try { obj.SetValue(property, value); } catch (ArgumentException) { _checks++; return; }
        throw new InvalidOperationException("FAIL: invalid " + property.Name + " was accepted");
    }
    private static void Layout(Grid root) { root.Measure(new Size(800, 400)); root.Arrange(new Rect(0, 0, 800, 400)); root.UpdateLayout(); }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-5;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("FAIL: " + message); _checks++; }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Save(Grid root, string outputDirectory, string name)
    {
        Layout(root); Pump(30);
        var bitmap = new RenderTargetBitmap(800, 400, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(outputDirectory); using var file = File.Create(Path.Combine(outputDirectory, name)); encoder.Save(file);
    }
}

internal sealed class Feedback : ObservableObject
{
    private RobotArmType _armType;
    public RobotArmType ArmType { get => _armType; set => SetProperty(ref _armType, value); }
    private int _armCount = 2;
    public int ArmCount { get => _armCount; set => SetProperty(ref _armCount, value); }
    private ObservableCollection<RobotArmModel> _arms = [];
    public ObservableCollection<RobotArmModel> Arms { get => _arms; set => SetProperty(ref _arms, value); }
    private RobotDirection _direction;
    public RobotDirection Direction { get => _direction; set => SetProperty(ref _direction, value); }
    private double _travel;
    public double Travel { get => _travel; set => SetProperty(ref _travel, value); }
    private double _reach = 150;
    public double Reach { get => _reach; set => SetProperty(ref _reach, value); }
    private bool _animate;
    public bool Animate { get => _animate; set => SetProperty(ref _animate, value); }
}
