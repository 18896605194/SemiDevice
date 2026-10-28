using System.IO;
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

namespace RobotControlPreview;

internal static class Program
{
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
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/xyz.Client.Presentation;component/{name}.xaml", UriKind.Relative) });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/RobotControlPreview;component/Localization/Strings.zh-CN.xaml", UriKind.Relative) });
            var preview = new Preview();
            if (args.Length == 0) { app.Run(preview); return 0; }

            // 隐藏的真实 WPF 宿主：不启动后端，不连接设备；截图、动作和几何检查均使用真实控件。
            var content = preview.Root;
            preview.Content = null;
            content.Margin = new Thickness(0);
            content.Width = 1128; content.Height = 808;
            using var host = new HwndSource(new HwndSourceParameters("Robot control preview checks")
            { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                PositionX = -32000, PositionY = -32000, Width = 1128, Height = 808 });
            host.RootVisual = content;
            content.Measure(new Size(content.Width, content.Height));
            content.Arrange(new Rect(0, 0, content.Width, content.Height));
            content.UpdateLayout(); Pump(120);
            var flat = preview.Flat; var solid = preview.Solid;
            var body = Find<RobotVisual3D>(solid).Single();
            Check(Find<DiskVisual3D>(body).Count() == 2, "uses the framework DiskVisual3D for both arms");
            Check(ReferenceEquals(Find<DiskVisual3D>(body).First().Data, preview.Arms[0].Wafer), "same WaferModel instance as 2D");
            Check(Find<Wafer>(body).Any(w => ReferenceEquals(w.Data, preview.Arms[0].Wafer)), "actual Wafer face receives original data");
            var meshes = FindMeshes(body).ToArray();
            Check(meshes.All(m => ((MeshGeometry3D)m.Geometry).IsFrozen), "frozen hardware geometry");

            // 同一个输入触发原运动轨道：先收回、再转向、最后伸出，3D 没有自己的动画计时器。
            flat.Rotation = solid.Rotation = RobotDirection.South;
            Pump(220);
            Check(solid.ArmVisuals[0].WaferTop > -150 && solid.ArmVisuals[0].Angle < 1,
                $"retracts before turning (loaded={solid.IsLoaded}, visible={solid.IsVisible}, reach={solid.ArmVisuals[0].WaferTop}, angle={solid.ArmVisuals[0].Angle})");
            Pump(420);
            Check(solid.ArmVisuals[0].Angle > 1 && solid.ArmVisuals[0].Angle < 180 && Math.Abs(solid.ArmVisuals[0].WaferTop + 24) < .01,
                "turns with retracted arms");
            Pump(1100);
            Check(Math.Abs(solid.ArmVisuals[0].Angle - 180) < .01 && Math.Abs(solid.ArmVisuals[0].WaferTop + 150) < .01,
                "extends after turn finishes");
            Check(meshes.Select(m => m.Geometry).Intersect(FindMeshes(body).Select(m => m.Geometry)).Count() >= meshes.Length - 1,
                "motion reuses the original meshes");
            flat.IsAnimationEnabled = solid.IsAnimationEnabled = false;
            flat.Rotation = solid.Rotation = RobotDirection.North;
            Save(content, args[0]);
            flat.ArmType = solid.ArmType = RobotArmType.FrogLeg;
            Pump(40);
            Save(content, Path.Combine(Path.GetDirectoryName(args[0])!, "robot-framework-frog.png"));
            preview.Arms[0].Wafer!.State = "Error";
            Check(Find<Wafer>(body).Any(w => w.Data is WaferModel wafer && wafer.State == "Error"), "wafer state updates in place");
            preview.Arms.RemoveAt(1); flat.ArmCount = solid.ArmCount = 1;
            Check(Find<DiskVisual3D>(body).Count() == 1, "arm count removes unused 3D parts");
            solid.Visibility = Visibility.Hidden;
            solid.Rotation = RobotDirection.East;
            solid.Visibility = Visibility.Visible; Pump(30);
            Check(Math.Abs(solid.ArmVisuals[0].Angle - 90) < .01, "visibility resumes at the current pose");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[0])!, "robot-preview-checks.txt"),
                "PASS: real Robot 2D/3D, original motion ordering, framework Wafer/DiskVisual3D data, mesh reuse, frog geometry, arm count, visibility.\n");
            app.Shutdown(); return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview-error.txt"), error.ToString());
            return 1;
        }
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static IEnumerable<GeometryModel3D> FindMeshes(DependencyObject root)
    {
        foreach (var visual in Find<ModelVisual3D>(root))
            foreach (var mesh in Geometry(visual.Content)) yield return mesh;
        static IEnumerable<GeometryModel3D> Geometry(Model3D? model)
        {
            if (model is GeometryModel3D mesh) yield return mesh;
            if (model is Model3DGroup group)
                foreach (var child in group.Children)
                    foreach (var meshChild in Geometry(child)) yield return meshChild;
        }
    }

    private static void Save(FrameworkElement content, string path)
    {
        content.Measure(new Size(content.Width, content.Height));
        content.Arrange(new Rect(0, 0, content.Width, content.Height));
        Pump(50); content.UpdateLayout();
        var image = new RenderTargetBitmap((int)content.Width, (int)content.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
