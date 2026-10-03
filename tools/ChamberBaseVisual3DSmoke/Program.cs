using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
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

            var scene = (Grid)Application.LoadComponent(new Uri("/ChamberBaseVisual3DSmoke;component/Scene.xaml", UriKind.Relative));
            var feedback = new BaseFeedback();
            scene.DataContext = feedback;
            using var host = new HwndSource(new HwndSourceParameters("Chamber base smoke")
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                PositionX = -32000, PositionY = -32000, Width = 580, Height = 410
            });
            host.RootVisual = scene;
            Pump(70);
            var plate = (ChamberBaseVisual3D)scene.FindName("Base");
            var root = (ModelVisual3D)scene.FindName("AssemblyRoot");
            var pipe = (FluidPipeVisual3D)scene.FindName("Pipe");
            var bowl = (BowlVisual3D)scene.FindName("Bowl");
            var arm = (ArmVisual3D)scene.FindName("Arm");
            var model = (Model3DGroup)plate.Content;
            var attachments = plate.Attachments.Cast<Visual3D>().ToArray();
            Point3D Position(Visual3D child) => child.TransformToAncestor(root).Transform(new Point3D());
            var initialPositions = attachments.Select(Position).ToArray();
            Check(attachments.Length == 4 && model.Bounds.Y < 0 && Near(model.Bounds.Y + model.Bounds.SizeY, 0), "XAML assembly mounts on a fixed top plane");
            var fullPreview = Render(scene);
            foreach (var child in attachments)
            {
                plate.Attachments.Remove(child);
            }

            ((TextBlock)scene.FindName("StateTitle")).Text = "ChamberBase · 独立底座";
            var basePreview = Render(scene);
            foreach (var child in attachments)
            {
                plate.Attachments.Add(child);
            }

            feedback.Thickness = 0.3;
            feedback.Length = 6.4;
            feedback.Width = 5.2;
            Pump(30);
            Check(Near(model.Bounds.SizeX, 6.4) && Near(model.Bounds.SizeZ, 5.2) && Near(model.Bounds.Y, -0.3), "dimensions bind and thickness extends downward");
            Check(attachments.Select((child, i) => ReferenceEquals(child, plate.Attachments[i]) && Position(child) == initialPositions[i]).All(x => x), "resizing preserves attachments and mounting positions");
            var mesh = ((GeometryModel3D)model.Children[0]).Geometry;
            var transform = new Transform3DGroup();
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 35)));
            transform.Children.Add(new TranslateTransform3D(3, 2, 1));
            var pipeBefore = Position(pipe);
            plate.Transform = transform;
            Check(attachments.Select((child, i) => (Position(child) - transform.Transform(initialPositions[i])).Length < 1e-7).All(x => x), "all mounted hardware inherits base rotation and translation");
            Check((Position(pipe) - transform.Transform(pipeBefore)).Length < 1e-7, "nested pipe also inherits whole assembly transform");
            Check(ReferenceEquals(mesh, ((GeometryModel3D)model.Children[0]).Geometry) && mesh.IsFrozen, "assembly movement reuses the frozen base mesh");
            var bowlTransform = bowl.Transform;
            arm.Angle = 0;
            Check(ReferenceEquals(bowl.Transform, bowlTransform) && !bowl.IsRaised, "Arm movement leaves sibling states unchanged");
            plate.IsSelected = true;
            Check(!bowl.IsSelected && !arm.IsSelected, "base selection does not select attached hardware");
            pipe.IsFlowing = true;
            Pump(60);
            Check(pipe.HasAnimatedProperties, "nested pipe starts in the mounted assembly");
            root.Children.Remove(plate);
            Check(!pipe.HasAnimatedProperties && pipe.IsFlowing, "removing base stops descendant animation while retaining feedback");
            root.Children.Add(plate);
            Pump(60);
            Check(pipe.HasAnimatedProperties, "reattaching base resumes descendant animation");
            scene.Visibility = Visibility.Hidden;
            Pump(30);
            Check(!pipe.HasAnimatedProperties, "hidden assembly stops descendant animation");
            pipe.IsFlowing = false;
            Check(BindingOperations.IsDataBound(plate, ChamberBaseVisual3D.ThicknessProperty), "resizing preserves external binding");
            foreach (var property in new[] { ChamberBaseVisual3D.LengthProperty, ChamberBaseVisual3D.WidthProperty, ChamberBaseVisual3D.ThicknessProperty })
            {
                foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity })
                {
                    bool rejected = false;
                    try
                    {
                        new ChamberBaseVisual3D().SetValue(property, invalid);
                    }
                    catch (ArgumentException)
                    {
                        rejected = true;
                    }

                    Check(rejected, "invalid dimensions rejected");
                }
            }

            if (args.Length > 0)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(basePreview, new Rect(0, 0, 580, 410));
                    dc.DrawImage(fullPreview, new Rect(582, 0, 580, 410));
                }
                var bitmap = new RenderTargetBitmap(1162, 410, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
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
            Console.WriteLine("PASS: ChamberBase dimensions, stable mounting plane, nested transforms, independent states, binding retention and descendant lifecycle.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
    private static BitmapSource Render(Grid scene)
    {
        scene.Measure(new Size(580, 410));
        scene.Arrange(new Rect(0, 0, 580, 410));
        scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap(580, 410, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        return bitmap;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
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

internal sealed class BaseFeedback : DependencyObject
{
    public double Length
    {
        get => (double)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }
    public static readonly DependencyProperty LengthProperty = DependencyProperty.Register(
        nameof(Length), typeof(double), typeof(BaseFeedback), new PropertyMetadata(5.6d));
    public double Width
    {
        get => (double)GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }
    public static readonly DependencyProperty WidthProperty = DependencyProperty.Register(
        nameof(Width), typeof(double), typeof(BaseFeedback), new PropertyMetadata(4.4d));
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(BaseFeedback), new PropertyMetadata(0.12d));
}
