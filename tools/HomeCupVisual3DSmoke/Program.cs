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
            foreach (string name in new[] { "DarkColors", "FontSize" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/xyz.Client.Presentation;component/Styles/{name}.xaml", UriKind.Relative) });
            }

            var scene = (Grid)Application.LoadComponent(new Uri("/HomeCupVisual3DSmoke;component/Scene.xaml", UriKind.Relative));
            var feedback = new CupFeedback();
            scene.DataContext = feedback;
            Flush(scene);
            var cup = (HomeCupVisual3D)scene.FindName("HomeCup");
            var model = (Model3DGroup)cup.Content;
            var title = (TextBlock)scene.FindName("StateTitle");
            var meshes = model.Children.Cast<GeometryModel3D>().Select(p => p.Geometry).ToArray();
            Color ColorOf() => ((SolidColorBrush)((DiffuseMaterial)((GeometryModel3D)model.Children[2]).Material).Brush).Color;
            Color idleColor = ColorOf();
            var idle = Render(scene);
            var placement = new TranslateTransform3D(1, 2, 3);
            cup.Transform = placement;
            feedback.IsDraining = true;
            Flush(scene);
            Check(cup.IsDraining && ColorOf() != idleColor && !cup.IsMoving && !cup.HasAnimatedProperties, "drain feedback highlights without creating movement or a clock");
            Check(ReferenceEquals(cup.Transform, placement) && Near(model.Bounds.Y, 0), "draining preserves fixed installation");
            Check(model.Children.Cast<GeometryModel3D>().Select((p, i) => ReferenceEquals(p.Geometry, meshes[i]) && p.Geometry.IsFrozen).All(x => x), "drain feedback reuses frozen geometry");
            var other = new HomeCupVisual3D();
            Check(!other.IsDraining, "separate instances remain independent");
            cup.Transform = Transform3D.Identity;
            title.Text = "接液 / 排液中 · 状态高亮";
            var draining = Render(scene);
            Check(BluePixels(draining) > BluePixels(idle) + 500, "actual WPF rendering shows active blue highlighting");
            cup.IsSelected = true;
            feedback.IsDraining = false;
            Flush(scene);
            Check(ColorOf() != idleColor, "selected cup stays highlighted after draining stops");
            cup.IsSelected = false;
            Check(ColorOf() == idleColor, "deselected idle cup restores theme material");
            feedback.IsDraining = true;
            feedback.Radius = 0.5;
            feedback.Height = 0.9;
            Flush(scene);
            Check(Near(model.Bounds.SizeX, 1) && Near(model.Bounds.SizeY, 0.9) && Near(model.Bounds.Y, 0), "bound dimensions preserve drain bottom origin");
            Check(ColorOf() != idleColor && cup.IsDraining, "resizing retains draining highlight");
            foreach (var part in model.Children.Cast<GeometryModel3D>())
            {
                var mesh = (MeshGeometry3D)part.Geometry;
                for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
                {
                    int a = mesh.TriangleIndices[i], b = mesh.TriangleIndices[i + 1], c = mesh.TriangleIndices[i + 2];
                    var p = mesh.Positions[a];
                    var q = mesh.Positions[b];
                    var r = mesh.Positions[c];
                    Check(Vector3D.DotProduct(Vector3D.CrossProduct(q - p, r - p), mesh.Normals[a]) > 0, "funnel, inner wall and drain normals face outward");
                    // 每个三角形都位于某个穿轴平面的同一侧，因此不会封住排液中心线。
                    var radial = new Vector3D(p.X + q.X + r.X, 0, p.Z + q.Z + r.Z);
                    Check(p.X * radial.X + p.Z * radial.Z > 0 && q.X * radial.X + q.Z * radial.Z > 0 && r.X * radial.X + r.Z * radial.Z > 0,
                        "open mouth and drain remain unobstructed along centerline");
                }
            }
            foreach (var property in new[] { HomeCupVisual3D.RadiusProperty, HomeCupVisual3D.HeightProperty })
            {
                foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity })
                {
                    bool rejected = false;
                    try
                    {
                        other.SetValue(property, invalid);
                    }
                    catch (ArgumentException)
                    {
                        rejected = true;
                    }

                    Check(rejected, "invalid dimension rejected");
                }
            }

            Check(BindingOperations.IsDataBound(cup, HomeCupVisual3D.IsDrainingProperty), "status binding preserved");
            if (args.Length > 0)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(idle, new Rect(0, 0, 360, 370));
                    dc.DrawImage(draining, new Rect(362, 0, 360, 370));
                }
                var bitmap = new RenderTargetBitmap(722, 370, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string output = Path.GetFullPath(args[0]);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var file = File.Create(output);
                encoder.Save(file);
                Console.WriteLine($"Preview: {output}");
            }
            app.Shutdown();
            Console.WriteLine("PASS: HomeCup XAML bindings, hollow drain, dimensions, fixed placement, independent state and rendered highlight.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Flush(Grid scene)
    {
        scene.Measure(new Size(360, 370));
        scene.Arrange(new Rect(0, 0, 360, 370));
        scene.UpdateLayout();
        scene.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
    private static BitmapSource Render(Grid scene)
    {
        Flush(scene);
        var bitmap = new RenderTargetBitmap(360, 370, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        return bitmap;
    }
    private static int BluePixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > pixels[i + 2] + 15)
            {
                count++;
            }
        }

        return count;
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-7;
    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }
}

internal sealed class CupFeedback : DependencyObject
{
    public bool IsDraining
    {
        get => (bool)GetValue(IsDrainingProperty);
        set => SetValue(IsDrainingProperty, value);
    }
    public static readonly DependencyProperty IsDrainingProperty = DependencyProperty.Register(
        nameof(IsDraining), typeof(bool), typeof(CupFeedback), new PropertyMetadata(false));
    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(CupFeedback), new PropertyMetadata(0.32d));
    public double Height
    {
        get => (double)GetValue(HeightProperty);
        set => SetValue(HeightProperty, value);
    }
    public static readonly DependencyProperty HeightProperty = DependencyProperty.Register(
        nameof(Height), typeof(double), typeof(CupFeedback), new PropertyMetadata(0.655d));
}
