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
            foreach (string dictionary in new[] { "DarkColors", "FontSize" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/xyz.Client.Presentation;component/Styles/{dictionary}.xaml", UriKind.Relative)
                });
            }

            var scene = (Grid)Application.LoadComponent(new Uri("/FluidPipeVisual3DSmoke;component/Scene.xaml", UriKind.Relative));
            var feedback = new PipeFeedback();
            scene.DataContext = feedback;
            // 隐藏的原生宿主提供真实 WPF 生命周期；没有 WS_VISIBLE，不在桌面显示窗口。
            using var host = new HwndSource(new HwndSourceParameters("FluidPipeVisual3DSmoke")
            {
                Width = 850, Height = 440, PositionX = -32000, PositionY = -32000,
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080
            });
            host.RootVisual = scene;
            var viewport = (Viewport3D)scene.FindName("SceneViewport");
            var arm = (ArmVisual3D)scene.FindName("Arm");
            var lift = (LiftVisual3D)scene.FindName("Lift");
            var diw = (FluidPipeVisual3D)scene.FindName("DiwPipe");
            var sc1 = (FluidPipeVisual3D)scene.FindName("Sc1Pipe");
            Layout(scene);
            Pump(100);
            Check(viewport.IsVisible, "test presentation source exposes a visible WPF viewport");
            Check(arm.Attachments.Count == 2 && diw.Length == arm.Length, "XAML mounts two pipes and binds their lengths");
            Check(!diw.IsFlowing && !sc1.IsFlowing && !diw.HasAnimatedProperties, "closed pipes do not animate");
            var idle = BodyColor(diw);

            feedback.DiwIsFlowing = true;
            Pump(150);
            Check(diw.IsFlowing && diw.HasAnimatedProperties && !sc1.HasAnimatedProperties && !sc1.IsFlowing,
                "DIW starts its own animation through one boolean, including inside Arm.Attachments");
            Check(BodyColor(diw) != idle && BodyColor(sc1) == idle, "only the active pipe highlights");
            var liquid = (Model3DGroup)((Model3DGroup)diw.Content).Children.Last();
            var meshes = liquid.Children.Cast<GeometryModel3D>().Select(part => part.Geometry).ToArray();
            var firstFrame = Render(scene);
            double firstX = ((GeometryModel3D)liquid.Children[2]).Bounds.X;
            Pump(120);
            var secondFrame = Render(scene);
            double secondX = ((GeometryModel3D)liquid.Children[2]).Bounds.X;
            Check(secondX != firstX && PixelDifference(firstFrame, secondFrame) > 20, "bright segments move spatially and rendered pixels change");
            Check(liquid.Children.Cast<GeometryModel3D>().Select((p, i) => ReferenceEquals(p.Geometry, meshes[i]) && p.Geometry.IsFrozen).All(same => same),
                "flow frames reuse static meshes");

            feedback.ArmAngle = 90;
            Pump(40);
            var tip = diw.TransformToAncestor(arm).Transform(new Point3D(diw.Length, 0, 0));
            Check(Near(tip.X, -0.075) && Near(tip.Z, -diw.Length), "pipes actually follow the Arm pivot");
            double beforeLift = arm.Transform.Transform(tip).Y;
            feedback.LiftIsRaised = true;
            Pump(600);
            Check(arm.Transform.Transform(tip).Y > beforeLift && diw.IsFlowing && !sc1.IsFlowing,
                "Lift moves the assembly without changing independent fluid states");
            feedback.ArmAngle = 0;
            Pump(40);
            Check(lift.IsRaised && diw.IsFlowing, "Arm Home does not lower Lift or close the pipe");

            scene.Visibility = Visibility.Hidden;
            Pump(80);
            Check(!diw.HasAnimatedProperties && diw.IsFlowing, "hidden pages stop clocks without changing feedback");
            scene.Visibility = Visibility.Visible;
            Pump(80);
            Check(diw.HasAnimatedProperties, "showing the page resumes animation");
            viewport.Children.Remove(arm);
            Pump(80);
            Check(!diw.HasAnimatedProperties, "removing the whole Arm stops descendant clocks");
            viewport.Children.Add(arm);
            Pump(80);
            Check(diw.HasAnimatedProperties, "reattaching the Arm resumes descendant animation");
            arm.Attachments.Remove(diw);
            Pump(60);
            Check(!diw.HasAnimatedProperties, "removing an individual pipe stops its clock");
            arm.Attachments.Add(diw);
            Pump(80);
            Check(diw.HasAnimatedProperties, "reattaching a pipe resumes its clock");

            diw.IsAnimationEnabled = false;
            Pump(50);
            Check(!diw.HasAnimatedProperties && diw.IsFlowing, "animation pause is independent of flow state");
            diw.IsAnimationEnabled = true;
            feedback.Sc1IsFlowing = true;
            Pump(60);
            feedback.DiwIsFlowing = false;
            Pump(60);
            Check(!diw.HasAnimatedProperties && sc1.HasAnimatedProperties && sc1.IsFlowing, "closing DIW leaves SC1 running");
            Check(((Model3DGroup)diw.Content).Children.Count == 2, "closing removes all liquid and ripple geometry");
            diw.IsSelected = true;
            Check(((Model3DGroup)diw.Content).Children.Count == 2, "selection does not create a liquid stream");
            diw.IsSelected = false;
            Check(BodyColor(diw) == idle, "closed unselected pipe restores metal color");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1d })
            {
                Reject(diw, FluidPipeVisual3D.StreamLengthProperty, invalid);
            }

            Reject(diw, FluidPipeVisual3D.LengthProperty, 0);
            sc1.StreamLength = 0;
            Check(((Model3DGroup)((Model3DGroup)sc1.Content).Children.Last()).Children.Count < meshes.Length,
                "zero stream distance suppresses falling liquid and the landing ripple");
            sc1.StreamLength = 0.55;
            Check(BindingOperations.IsDataBound(diw, FluidPipeVisual3D.IsFlowingProperty), "flow binding survives all transitions");

            feedback.LiftIsRaised = false;
            feedback.Sc1IsFlowing = false;
            feedback.DiwIsFlowing = true;
            Pump(650);
            if (args.Length > 0)
            {
                Save(Render(scene), args[0]);
            }

            feedback.DiwIsFlowing = false;
            host.RootVisual = null;
            Console.WriteLine("PASS: pipe flow pixels, independent channels, Arm/Lift attachment, visibility/detach lifecycle and input validation.");
            app.Shutdown();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static Color BodyColor(FluidPipeVisual3D pipe) =>
        ((SolidColorBrush)((DiffuseMaterial)((GeometryModel3D)((Model3DGroup)pipe.Content).Children[0]).Material).Brush).Color;
    private static void Layout(Grid scene) { scene.Measure(new Size(850, 440)); scene.Arrange(new Rect(0, 0, 850, 440)); scene.UpdateLayout(); }
    private static RenderTargetBitmap Render(Grid scene)
    {
        Layout(scene);
        var bitmap = new RenderTargetBitmap(850, 440, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        return bitmap;
    }
    private static int PixelDifference(BitmapSource a, BitmapSource b)
    {
        var x = new byte[a.PixelWidth * a.PixelHeight * 4]; var y = new byte[x.Length];
        a.CopyPixels(x, a.PixelWidth * 4, 0); b.CopyPixels(y, b.PixelWidth * 4, 0);
        return x.Where((value, i) => Math.Abs(value - y[i]) > 10).Count();
    }
    private static void Save(BitmapSource bitmap, string output)
    {
        string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream); Console.WriteLine($"Preview: {path}");
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-7;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(DependencyObject target, DependencyProperty property, double value)
    {
        try { target.SetValue(property, value); } catch (ArgumentException) { return; }
        throw new InvalidOperationException($"Invalid {property.Name} accepted");
    }
}

internal sealed class PipeFeedback : DependencyObject
{
    public bool DiwIsFlowing { get => (bool)GetValue(DiwIsFlowingProperty); set => SetValue(DiwIsFlowingProperty, value); }
    public static readonly DependencyProperty DiwIsFlowingProperty = DependencyProperty.Register(nameof(DiwIsFlowing), typeof(bool), typeof(PipeFeedback), new PropertyMetadata(false));
    public bool Sc1IsFlowing { get => (bool)GetValue(Sc1IsFlowingProperty); set => SetValue(Sc1IsFlowingProperty, value); }
    public static readonly DependencyProperty Sc1IsFlowingProperty = DependencyProperty.Register(nameof(Sc1IsFlowing), typeof(bool), typeof(PipeFeedback), new PropertyMetadata(false));
    public double ArmAngle { get => (double)GetValue(ArmAngleProperty); set => SetValue(ArmAngleProperty, value); }
    public static readonly DependencyProperty ArmAngleProperty = DependencyProperty.Register(nameof(ArmAngle), typeof(double), typeof(PipeFeedback), new PropertyMetadata(0d));
    public bool LiftIsRaised { get => (bool)GetValue(LiftIsRaisedProperty); set => SetValue(LiftIsRaisedProperty, value); }
    public static readonly DependencyProperty LiftIsRaisedProperty = DependencyProperty.Register(nameof(LiftIsRaised), typeof(bool), typeof(PipeFeedback), new PropertyMetadata(false));
}
