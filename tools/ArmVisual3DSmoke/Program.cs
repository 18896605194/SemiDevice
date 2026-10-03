using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using xyz.Client.Presentation.Controls.ThreeD;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application();
            foreach (string dictionary in new[] { "DarkColors", "FontSize" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/xyz.Client.Presentation;component/Styles/{dictionary}.xaml", UriKind.Relative)
                });
            }

            var scene = (Grid)Application.LoadComponent(new Uri("/ArmVisual3DSmoke;component/Scene.xaml", UriKind.Relative));
            var arm = (ArmVisual3D)scene.FindName("Arm");
            var feedback = new ArmFeedback { Angle = 25 };
            scene.DataContext = feedback;
            FlushBindings(scene);
            Check(arm.Angle == 25 && !arm.IsMoving, "XAML binds external feedback through the scene");

            var model = (Model3DGroup)arm.Content;
            var parts = model.Children.Cast<GeometryModel3D>().ToArray();
            var meshes = parts.Select(part => part.Geometry).ToArray();
            Color idle = ColorOf(parts[2]);
            var other = new ArmVisual3D();
            Color otherIdle = ColorOf((GeometryModel3D)((Model3DGroup)other.Content).Children[2]);

            feedback.IsMoving = true;
            FlushBindings(scene);
            Color moving = ColorOf(parts[2]);
            Check(arm.IsMoving && moving.B > idle.B && moving != idle && !arm.IsSelected,
                "moving alone highlights the arm");
            Check(ColorOf((GeometryModel3D)((Model3DGroup)other.Content).Children[2]) == otherIdle,
                "instances do not share mutable appearance");

            var placement = new TranslateTransform3D(7, 3, -2);
            arm.Transform = placement;
            feedback.Angle = 90;
            FlushBindings(scene);
            Point3D tip = model.Transform.Transform(new Point3D(arm.Length, 0, 0));
            Check(Near(tip.X, 0) && Near(tip.Y, 0) && Near(tip.Z, -arm.Length), "rotation uses the documented pivot and direction");
            Check(ReferenceEquals(arm.Transform, placement) && Near(arm.Transform.Transform(tip).Y, 3),
                "rotation does not change external mounting or Lift height");
            Check(parts.Select((part, index) => ReferenceEquals(part.Geometry, meshes[index])).All(same => same),
                "motion and highlighting reuse meshes");

            arm.IsSelected = true;
            feedback.IsMoving = false;
            FlushBindings(scene);
            Check(ColorOf(parts[2]) == moving, "selection remains highlighted after motion stops");
            arm.IsSelected = false;
            Check(ColorOf(parts[2]) == idle, "stop and deselect restore the original material");
            feedback.Angle = 0;
            FlushBindings(scene);
            Check(!arm.IsMoving && ColorOf(parts[2]) == idle && ReferenceEquals(arm.Transform, placement),
                "angle updates and Home do not imply moving or lowering");

            feedback.IsMoving = true;
            arm.Length = 3.2;
            arm.Width = 0.3;
            arm.Thickness = 0.1;
            arm.PivotRadius = 0.32;
            FlushBindings(scene);
            Check(model.Bounds.SizeX > 3.2 && arm.IsMoving && ColorOf((GeometryModel3D)model.Children[2]) == moving,
                "geometry changes retain motion appearance");
            Check(BindingOperations.IsDataBound(arm, ArmVisual3D.AngleProperty)
                && BindingOperations.IsDataBound(arm, HardwareVisual3D.IsMovingProperty), "updates retain external bindings");
            CheckMeshes(model);

            foreach (var property in new[] { ArmVisual3D.LengthProperty, ArmVisual3D.WidthProperty,
                         ArmVisual3D.ThicknessProperty, ArmVisual3D.PivotRadiusProperty })
            {
                foreach (double invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
                    Reject(arm, property, invalid);
            }
            Reject(arm, ArmVisual3D.AngleProperty, double.NaN);
            Reject(arm, ArmVisual3D.AngleProperty, double.NegativeInfinity);
            Reject(arm, HardwareVisual3D.HighlightStrengthProperty, 1.1);
            Reject(arm, HardwareVisual3D.HighlightStrengthProperty, -0.1);

            var replacement = new ArmFeedback { Angle = -35 };
            scene.DataContext = replacement;
            FlushBindings(scene);
            Check(arm.Angle == -35 && !arm.IsMoving, "replacing DataContext rebinds both feedback properties");
            replacement.IsMoving = true;
            Check(arm.HighlightColor == ((SolidColorBrush)app.Resources["DarkAccent"]).Color,
                "XAML uses the application accent resource");
            // 主题资源由 WPF 冻结；用外部可变颜色源验证运行时换色，不修改全局资源。
            var accent = new SolidColorBrush(arm.HighlightColor);
            BindingOperations.SetBinding(arm, HardwareVisual3D.HighlightColorProperty,
                new Binding(nameof(SolidColorBrush.Color)) { Source = accent, Mode = BindingMode.OneWay });
            accent.Color = Colors.Orange;
            FlushBindings(scene);
            Check(arm.HighlightColor == Colors.Orange && ColorOf((GeometryModel3D)model.Children[2]) != moving,
                "theme accent binding updates the active material");
            accent.Color = Color.FromRgb(66, 165, 245);

            if (args.Length > 0)
            {
                arm.ClearValue(Visual3D.TransformProperty);
                arm.ClearValue(ArmVisual3D.LengthProperty);
                arm.ClearValue(ArmVisual3D.WidthProperty);
                arm.ClearValue(ArmVisual3D.ThicknessProperty);
                arm.ClearValue(ArmVisual3D.PivotRadiusProperty);
                replacement.Angle = 0;
                SaveComparison(scene, replacement, args[0]);
            }
            Console.WriteLine("PASS: Arm binding, rotation, independent highlight, theme, geometry, and input validation.");
            app.Shutdown();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void CheckMeshes(Model3DGroup model)
    {
        foreach (var part in model.Children.Cast<GeometryModel3D>())
        {
            var mesh = (MeshGeometry3D)part.Geometry;
            Check(mesh.IsFrozen && part.Material.IsFrozen, "static meshes and materials are frozen");
            for (int index = 0; index < mesh.TriangleIndices.Count; index += 3)
            {
                int a = mesh.TriangleIndices[index];
                int b = mesh.TriangleIndices[index + 1];
                int c = mesh.TriangleIndices[index + 2];
                Vector3D cross = Vector3D.CrossProduct(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                Check(double.IsFinite(cross.Length) && cross.Length > 0, "triangles are finite and nondegenerate");
                Check(Vector3D.DotProduct(cross, mesh.Normals[a]) > 0, "normals match face winding");
                var bounds = mesh.Bounds;
                var center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
                Check(Vector3D.DotProduct(cross, mesh.Positions[a] - center) > 0, "solid faces point outward");
            }
        }
    }

    private static void SaveComparison(Grid scene, ArmFeedback feedback, string output)
    {
        var title = (TextBlock)scene.FindName("StateTitle");
        feedback.IsMoving = false;
        title.Text = "静止 · 默认材质";
        var idle = Render(scene);
        feedback.IsMoving = true;
        title.Text = "动作中 · 自动高亮";
        var moving = Render(scene);
        var pixels = new byte[idle.PixelWidth * idle.PixelHeight * 4];
        var highlighted = new byte[pixels.Length];
        idle.CopyPixels(pixels, idle.PixelWidth * 4, 0);
        moving.CopyPixels(highlighted, moving.PixelWidth * 4, 0);
        int changed = 0;
        // 只检查三维区域，排除标题变化导致的假阳性。
        for (int y = 56; y < 300; y++)
            for (int x = 0; x < idle.PixelWidth; x++)
            {
                int offset = (y * idle.PixelWidth + x) * 4;
                if (Math.Abs(pixels[offset] - highlighted[offset]) > 10) changed++;
            }
        Check(changed > 1000, "rendered arm visibly changes with IsMoving");

        var comparison = new DrawingVisual();
        using (DrawingContext drawing = comparison.RenderOpen())
        {
            drawing.DrawImage(idle, new Rect(0, 0, 760, 340));
            drawing.DrawImage(moving, new Rect(0, 342, 760, 340));
        }
        var bitmap = new RenderTargetBitmap(760, 682, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(comparison);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        encoder.Save(file);
        Console.WriteLine($"Preview: {path}");
    }

    private static RenderTargetBitmap Render(Grid scene)
    {
        FlushBindings(scene);
        var bitmap = new RenderTargetBitmap(760, 340, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        return bitmap;
    }

    private static void FlushBindings(Grid scene)
    {
        scene.Measure(new Size(760, 340));
        scene.Arrange(new Rect(0, 0, 760, 340));
        scene.UpdateLayout();
        scene.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static Color ColorOf(GeometryModel3D part) => ((SolidColorBrush)((DiffuseMaterial)part.Material).Brush).Color;
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-8;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private static void Reject(DependencyObject target, DependencyProperty property, double value)
    {
        try { target.SetValue(property, value); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException($"{property.Name} accepted invalid input: {value}");
    }
}

// 仅作为测试反馈源，不连接服务或设备。
internal sealed class ArmFeedback : DependencyObject
{
    public double Angle
    {
        get => (double)GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle), typeof(double), typeof(ArmFeedback), new PropertyMetadata(0d));

    public bool IsMoving
    {
        get => (bool)GetValue(IsMovingProperty);
        set => SetValue(IsMovingProperty, value);
    }
    public static readonly DependencyProperty IsMovingProperty = DependencyProperty.Register(
        nameof(IsMoving), typeof(bool), typeof(ArmFeedback), new PropertyMetadata(false));
}
