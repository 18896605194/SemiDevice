using System.IO;
using System.Reflection;
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


            var scene = (Grid)Application.LoadComponent(new Uri("/LiftVisual3DSmoke;component/Scene.xaml", UriKind.Relative));
            var feedback = new CylinderFeedback();
            scene.DataContext = feedback;
            var lift = (LiftVisual3D)scene.FindName("Lift");
            Flush(scene);
            var model = (Model3DGroup)lift.Content;
            var parts = model.Children.Cast<GeometryModel3D>().ToArray();
            var meshes = parts.Select(part => part.Geometry).ToArray();
            var fixedBounds = parts.Take(4).Select(part => part.Bounds).ToArray();
            Color idle = ColorOf(parts[2]);
            double down = lift.MountHeight;
            var initialUp = new LiftVisual3D { IsRaised = true };
            double up = initialUp.MountHeight;
            Check(up > down && !initialUp.HasAnimatedProperties, "initial true is shown immediately before attaching to a scene");
            Check(ColorOf((GeometryModel3D)((Model3DGroup)initialUp.Content).Children[2]) == idle,
                "initial state does not imply motion");
            foreach (string name in new[] { "Position", "Stroke", "Progress", "ExtensionHeight" })
            {
                Check(typeof(LiftVisual3D).GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is null,
                    $"continuous motion parameter {name} is not public");
            }

            Check(LiftVisual3D.MountHeightProperty.ReadOnly, "assembly height is read-only");

            var mount = new TranslateTransform3D();
            BindingOperations.SetBinding(mount, TranslateTransform3D.OffsetYProperty,
                new Binding(nameof(LiftVisual3D.MountHeight)) { Source = lift });
            var arm = new ArmVisual3D { Angle = 27, Transform = mount };
            var placement = new TranslateTransform3D(3, 2, 1);
            lift.Transform = placement;

            feedback.IsRaised = true;
            Flush(scene);
            Check(lift.IsRaised && !lift.IsMoving && ColorOf(parts[2]) != idle,
                "one boolean starts an animation and automatic highlight without IsMoving input");
            Pump(170);
            Check(lift.MountHeight > down && lift.MountHeight < up, "the rod has an intermediate rising position");
            Check(Near(mount.OffsetY, lift.MountHeight) && arm.Angle == 27 && !arm.IsMoving,
                "assembly follows the mounting height without rotating or marking the Arm as moving");
            double beforeReverse = lift.MountHeight;
            feedback.IsRaised = false;
            Flush(scene);
            Check(Math.Abs(lift.MountHeight - beforeReverse) < 0.08, "rapid reversal starts at the current position");
            Check(ColorOf(parts[2]) != idle, "reversal keeps the moving highlight");
            Pump(550);
            Check(Near(lift.MountHeight, down) && ColorOf(parts[2]) == idle && !lift.HasAnimatedProperties,
                "reverse completes at the lower stop, restores material and removes the animation clock");

            feedback.IsRaised = true;
            Pump(600);
            Check(Near(lift.MountHeight, up) && ColorOf(parts[2]) == idle && !lift.HasAnimatedProperties,
                "true finishes at the upper stop and restores the material");
            arm.Angle = 0;
            Check(lift.IsRaised && Near(lift.MountHeight, up), "Arm Home does not lower the Lift");
            lift.IsSelected = true;
            feedback.IsRaised = false;
            Pump(600);
            Check(Near(lift.MountHeight, down) && ColorOf(parts[2]) != idle, "selection highlight survives animation completion");
            lift.IsSelected = false;
            Check(ColorOf(parts[2]) == idle, "deselect restores the default material");

            var movingSource = new CylinderFeedback();
            BindingOperations.SetBinding(lift, HardwareVisual3D.IsMovingProperty,
                new Binding(nameof(CylinderFeedback.IsRaised)) { Source = movingSource });
            movingSource.IsRaised = true;
            feedback.IsRaised = true;
            Pump(600);
            Check(lift.IsMoving && ColorOf(parts[2]) != idle,
                "optional real moving feedback remains highlighted after the visual transition");
            movingSource.IsRaised = false;
            Flush(scene);
            Check(ColorOf(parts[2]) == idle && BindingOperations.IsDataBound(lift, HardwareVisual3D.IsMovingProperty),
                "internal animation does not overwrite the external moving binding");
            Check(parts.Select((part, index) => ReferenceEquals(part.Geometry, meshes[index]) && part.Geometry.IsFrozen).All(same => same),
                "all animation frames reuse frozen meshes");
            Check(parts.Take(4).Select((part, index) => part.Bounds == fixedBounds[index]).All(same => same),
                "the base and body remain stationary");
            Check(ReferenceEquals(lift.Transform, placement), "the external mounting transform is preserved");
            Check(BindingOperations.IsDataBound(lift, LiftVisual3D.IsRaisedProperty), "the boolean binding is preserved");

            scene.DataContext = new CylinderFeedback();
            Pump(600);
            Check(!lift.IsRaised && Near(lift.MountHeight, down), "replacing the feedback source updates the cylinder");
            if (args.Length > 0)
            {
                scene.DataContext = feedback;
                feedback.IsRaised = false;
                lift.ClearValue(Visual3D.TransformProperty);
                Pump(600);
                SaveComparison(scene, feedback, args[0]);
            }
            Console.WriteLine("PASS: Lift boolean binding, rise/fall, reversal, highlight, independent assembly and mesh reuse.");
            app.Shutdown();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void SaveComparison(Grid scene, CylinderFeedback feedback, string output)
    {
        var title = (TextBlock)scene.FindName("StateTitle");
        var lift = (LiftVisual3D)scene.FindName("Lift");
        title.Text = "下位 · 静止";
        var down = Render(scene);
        feedback.IsRaised = true;
        Pump(180);
        title.Text = "上升中 · 自动高亮";
        var moving = Render(scene);
        Check(lift.HasAnimatedProperties, "middle preview is captured during the actual transition");
        Pump(500);
        title.Text = "上位 · 静止";
        var up = Render(scene);
        Check(BluePixels(moving) > BluePixels(down) + 1000 && BluePixels(up) == BluePixels(down),
            "only the moving preview has visible blue highlight");

        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawImage(down, new Rect(0, 0, 320, 420));
            drawing.DrawImage(moving, new Rect(322, 0, 320, 420));
            drawing.DrawImage(up, new Rect(644, 0, 320, 420));
        }
        var bitmap = new RenderTargetBitmap(964, 420, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        encoder.Save(file);
        Console.WriteLine($"Preview: {path}");
    }

    private static int BluePixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int count = 0;
        for (int y = 72; y < 380; y++)
        {
            for (int x = 0; x < bitmap.PixelWidth; x++)
            {
                int offset = (y * bitmap.PixelWidth + x) * 4;
                if (pixels[offset] > pixels[offset + 2] + 15)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static RenderTargetBitmap Render(Grid scene)
    {
        Flush(scene);
        var bitmap = new RenderTargetBitmap(320, 420, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        return bitmap;
    }

    private static void Flush(Grid scene)
    {
        scene.Measure(new Size(320, 420));
        scene.Arrange(new Rect(0, 0, 320, 420));
        scene.UpdateLayout();
        scene.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static Color ColorOf(GeometryModel3D part) => ((SolidColorBrush)((DiffuseMaterial)part.Material).Brush).Color;
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-7;
    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }
}

internal sealed class CylinderFeedback : DependencyObject
{
    public bool IsRaised
    {
        get => (bool)GetValue(IsRaisedProperty);
        set => SetValue(IsRaisedProperty, value);
    }
    public static readonly DependencyProperty IsRaisedProperty = DependencyProperty.Register(
        nameof(IsRaised), typeof(bool), typeof(CylinderFeedback), new PropertyMetadata(false));
}
