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
    private static int _checks;

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
            using var host = new HwndSource(new HwndSourceParameters("FluidPipeVisual3DSmoke")
            {
                Width = 850,
                Height = 440,
                PositionX = -32000,
                PositionY = -32000,
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08000080
            });
            host.RootVisual = scene;
            var viewport = (Viewport3D)scene.FindName("SceneViewport");
            var arm = (ArmVisual3D)scene.FindName("Arm");
            var lift = (LiftVisual3D)scene.FindName("Lift");
            var diw = (FluidPipeVisual3D)scene.FindName("DiwPipe");
            var sc1 = (FluidPipeVisual3D)scene.FindName("Sc1Pipe");
            FluidRouteVisual3D tube = Route(diw, 0);
            FluidRouteVisual3D jet = Route(diw, 1);
            Layout(scene);
            Pump(100);

            Check(viewport.IsVisible, "test source exposes a visible WPF viewport");
            Check(arm.Attachments.Count == 2 && diw.Length == arm.Length, "XAML mounts two independently bound nozzles");
            Check(!diw.IsFlowing && !sc1.IsFlowing && !IsAnimating(diw), "closed nozzles do not animate");
            Check(tube.Path!.Count == 3 && tube.BendRadius > 0 && tube.WallOpacity == 0.18,
                "nozzle uses the shared transparent route with a round downward bend");
            Check(jet.WallOpacity == 0 && Near(jet.Path![0].Y, -FluidPipeVisual3D.OutletDrop),
                "external jet starts exactly at the nozzle and has no visible pipe wall");

            Color wallColor = tube.WallColor;
            var stopped = Render(scene);
            feedback.DiwIsFlowing = true;
            Pump(150);
            Check(diw.IsFlowing && IsAnimating(diw) && !IsAnimating(sc1) && !sc1.IsFlowing,
                "DIW starts independently through its existing boolean binding");
            Check(tube.IsFlowing && jet.IsFlowing && tube.WallColor == wallColor
                && Layers(Route(sc1, 0)) == 4,
                "active liquid is separate from neutral walls and the closed channel stays empty");

            var meshes = Meshes(diw);
            var firstFrame = Render(scene);
            Pump(150);
            Check(PixelDifference(firstFrame, Render(scene)) > 20, "water highlights actually move in rendered Arm pixels");
            Check(Meshes(diw).SequenceEqual(meshes) && meshes.All(mesh => mesh.IsFrozen),
                "animation reuses the frozen tube and jet meshes");

            feedback.DiwIsFlowing = false;
            Pump(60);
            Check(!IsAnimating(diw) && Layers(tube) == 4 && Layers(jet) == 4
                && ((Model3DGroup)diw.Content).Children.Count == 0,
                "closing removes tube liquid, external liquid and ripple");
            Check(PixelDifference(stopped, Render(scene)) == 0, "closing restores the original empty-pipe pixels");
            feedback.DiwIsFlowing = true;
            Pump(60);

            feedback.ArmAngle = 90;
            Pump(40);
            var tip = diw.TransformToAncestor(arm).Transform(new Point3D(diw.Length, 0, 0));
            Check(Near(tip.X, -0.075) && Near(tip.Z, -diw.Length), "both tube and jet follow the Arm pivot");
            double beforeLift = arm.Transform.Transform(tip).Y;
            feedback.LiftIsRaised = true;
            Pump(600);
            Check(arm.Transform.Transform(tip).Y > beforeLift && diw.IsFlowing && !sc1.IsFlowing,
                "Lift moves the assembly without changing independent fluid states");
            feedback.ArmAngle = 0;
            Pump(40);
            Check(lift.IsRaised && diw.IsFlowing, "Arm Home does not lower Lift or close a nozzle");

            scene.Visibility = Visibility.Hidden;
            Pump(80);
            Check(!IsAnimating(diw) && diw.IsFlowing, "hidden page stops every nozzle clock without changing feedback");
            scene.Visibility = Visibility.Visible;
            Pump(80);
            Check(IsAnimating(diw), "showing the page resumes water");
            viewport.Children.Remove(arm);
            Pump(80);
            Check(!IsAnimating(diw), "removing the whole Arm stops every descendant clock");
            viewport.Children.Add(arm);
            Pump(80);
            Check(IsAnimating(diw), "reattaching the Arm resumes water");
            arm.Attachments.Remove(diw);
            Pump(60);
            Check(!IsAnimating(diw), "removing one nozzle stops its clocks");
            arm.Attachments.Add(diw);
            Pump(80);
            Check(IsAnimating(diw), "reattaching one nozzle resumes water");

            diw.IsAnimationEnabled = false;
            Pump(60);
            var paused = Render(scene);
            Pump(100);
            Check(!IsAnimating(diw) && diw.IsFlowing && Layers(tube) == 5 && Layers(jet) == 5
                && PixelDifference(paused, Render(scene)) == 0,
                "animation pause preserves liquid and freezes all water pixels");
            diw.FlowColor = Colors.CornflowerBlue;
            Check(tube.FlowColor == diw.FlowColor && jet.FlowColor == diw.FlowColor,
                "flow color is shared by tube and external jet");
            diw.FlowColor = Color.FromRgb(82, 212, 239);
            diw.IsAnimationEnabled = true;

            Geometry3D originalTubeMesh = ((GeometryModel3D)((Model3DGroup)tube.Content).Children[0]).Geometry;
            diw.StreamLength = 1.1;
            Check(Near(jet.Path![1].Y, -FluidPipeVisual3D.OutletDrop - 1.1)
                && ReferenceEquals(originalTubeMesh, ((GeometryModel3D)((Model3DGroup)tube.Content).Children[0]).Geometry),
                "moving the landing surface updates jet length without rebuilding the supply tube");
            diw.StreamLength = 0.55;

            feedback.Sc1IsFlowing = true;
            feedback.DiwIsFlowing = false;
            Pump(60);
            Check(!IsAnimating(diw) && IsAnimating(sc1) && sc1.IsFlowing, "closing DIW leaves SC1 running");
            diw.IsSelected = true;
            Check(Layers(tube) == 4 && !jet.IsFlowing, "selection does not create fluid in a closed nozzle");
            diw.IsSelected = false;

            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1d })
            {
                Reject(diw, FluidPipeVisual3D.StreamLengthProperty, invalid);
            }
            Reject(diw, FluidPipeVisual3D.LengthProperty, 0);
            sc1.StreamLength = 0;
            Check(Route(sc1, 0).IsFlowing && !Route(sc1, 1).IsFlowing && Layers(Route(sc1, 1)) == 0
                && ((Model3DGroup)sc1.Content).Children.Count == 0,
                "zero stream distance leaves only the water inside the supply tube");
            sc1.StreamLength = 0.55;
            Check(BindingOperations.IsDataBound(diw, FluidPipeVisual3D.IsFlowingProperty),
                "nozzle feedback binding survives every transition");

            feedback.LiftIsRaised = false;
            feedback.DiwIsFlowing = true;
            feedback.Sc1IsFlowing = true;
            Pump(650);
            if (args.Length > 0)
            {
                Save(Render(scene), args[0]);
            }

            feedback.DiwIsFlowing = false;
            feedback.Sc1IsFlowing = false;
            host.RootVisual = null;
            app.Shutdown();
            Console.WriteLine($"PASS: {_checks} transparent-nozzle, water, independent-channel and Arm/Lift checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static FluidRouteVisual3D Route(FluidPipeVisual3D pipe, int index)
    {
        return (FluidRouteVisual3D)pipe.Children[index];
    }

    private static int Layers(FluidRouteVisual3D route)
    {
        return ((Model3DGroup)route.Content).Children.Count;
    }

    private static bool IsAnimating(FluidPipeVisual3D pipe)
    {
        return pipe.HasAnimatedProperties || Route(pipe, 0).HasAnimatedProperties || Route(pipe, 1).HasAnimatedProperties;
    }

    private static List<Geometry3D> Meshes(FluidPipeVisual3D pipe)
    {
        var meshes = new List<Geometry3D>();
        foreach (FluidRouteVisual3D route in pipe.Children)
        {
            foreach (GeometryModel3D part in ((Model3DGroup)route.Content).Children)
            {
                meshes.Add(part.Geometry);
            }
        }

        return meshes;
    }

    private static void Layout(Grid scene)
    {
        scene.Measure(new Size(850, 440));
        scene.Arrange(new Rect(0, 0, 850, 440));
        scene.UpdateLayout();
    }

    private static RenderTargetBitmap Render(Grid scene)
    {
        Layout(scene);
        var bitmap = new RenderTargetBitmap(850, 440, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        return bitmap;
    }

    private static int PixelDifference(BitmapSource first, BitmapSource second)
    {
        byte[] firstPixels = new byte[first.PixelWidth * first.PixelHeight * 4];
        byte[] secondPixels = new byte[firstPixels.Length];
        first.CopyPixels(firstPixels, first.PixelWidth * 4, 0);
        second.CopyPixels(secondPixels, second.PixelWidth * 4, 0);
        int changed = 0;
        for (int index = 0; index < firstPixels.Length; index++)
        {
            if (Math.Abs(firstPixels[index] - secondPixels[index]) > 10)
            {
                changed++;
            }
        }

        return changed;
    }

    private static void Save(BitmapSource bitmap, string output)
    {
        string path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"Preview: {path}");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static bool Near(double first, double second)
    {
        return Math.Abs(first - second) < 0.0000001;
    }

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }

        _checks++;
        Console.WriteLine("OK: " + message);
    }

    private static void Reject(DependencyObject target, DependencyProperty property, double value)
    {
        try
        {
            target.SetValue(property, value);
        }
        catch (ArgumentException)
        {
            _checks++;
            return;
        }

        throw new InvalidOperationException($"Invalid {property.Name} accepted");
    }
}

internal sealed class PipeFeedback : DependencyObject
{
    public bool DiwIsFlowing
    {
        get
        {
            return (bool)GetValue(DiwIsFlowingProperty);
        }
        set
        {
            SetValue(DiwIsFlowingProperty, value);
        }
    }

    public static readonly DependencyProperty DiwIsFlowingProperty = DependencyProperty.Register(
        nameof(DiwIsFlowing), typeof(bool), typeof(PipeFeedback), new PropertyMetadata(false));

    public bool Sc1IsFlowing
    {
        get
        {
            return (bool)GetValue(Sc1IsFlowingProperty);
        }
        set
        {
            SetValue(Sc1IsFlowingProperty, value);
        }
    }

    public static readonly DependencyProperty Sc1IsFlowingProperty = DependencyProperty.Register(
        nameof(Sc1IsFlowing), typeof(bool), typeof(PipeFeedback), new PropertyMetadata(false));

    public double ArmAngle
    {
        get
        {
            return (double)GetValue(ArmAngleProperty);
        }
        set
        {
            SetValue(ArmAngleProperty, value);
        }
    }

    public static readonly DependencyProperty ArmAngleProperty = DependencyProperty.Register(
        nameof(ArmAngle), typeof(double), typeof(PipeFeedback), new PropertyMetadata(0d));

    public bool LiftIsRaised
    {
        get
        {
            return (bool)GetValue(LiftIsRaisedProperty);
        }
        set
        {
            SetValue(LiftIsRaisedProperty, value);
        }
    }

    public static readonly DependencyProperty LiftIsRaisedProperty = DependencyProperty.Register(
        nameof(LiftIsRaised), typeof(bool), typeof(PipeFeedback), new PropertyMetadata(false));

}
