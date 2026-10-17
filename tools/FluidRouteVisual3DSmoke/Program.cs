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
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/xyz.Client.Presentation;component/Styles/DarkColors.xaml", UriKind.Relative)
            });

            var scene = (Grid)Application.LoadComponent(
                new Uri("/FluidRouteVisual3DSmoke;component/Scene.xaml", UriKind.Relative));
            var feedback = new Feedback();
            scene.DataContext = feedback;

            // 使用不可见窗口承载真实 WPF 场景，测试不会弹出操作窗口。
            using var host = new HwndSource(new HwndSourceParameters("FluidRouteSmoke")
            {
                Width = 900,
                Height = 450,
                PositionX = -32000,
                PositionY = -32000,
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08000080
            });
            host.RootVisual = scene;

            var viewport = (Viewport3D)scene.FindName("Viewport");
            var pipe = (FluidRouteVisual3D)scene.FindName("Pipe");
            var group = (Model3DGroup)pipe.Content;
            Layout(scene);
            Pump(100);

            Check(viewport.IsVisible && group.Children.Count == 4,
                "stopped route initially shows only hollow pipe walls");
            Check(pipe.Path!.Count == 4 && !pipe.HasAnimatedProperties,
                "path and feedback binding start correctly");
            Check(new FluidRouteVisual3D().Path != new FluidRouteVisual3D().Path,
                "instances do not share mutable paths");

            var still = Render(scene);
            feedback.Flowing = true;
            Pump(130);
            var meshes = group.Children.Cast<GeometryModel3D>().Select(model => model.Geometry).ToArray();
            var flowing = Render(scene);
            Pump(170);
            var later = Render(scene);

            Check(pipe.HasAnimatedProperties && Difference(flowing, later) > 40,
                "water visibly flows inside the pipe");
            Check(group.Children.Cast<GeometryModel3D>().Select(
                (model, index) => ReferenceEquals(meshes[index], model.Geometry) && model.Geometry.IsFrozen).All(value => value),
                "animation reuses frozen geometry");

            TranslateTransform offset = FlowOffset(pipe);
            double forward = offset.X;
            pipe.ReverseFlow = true;
            Pump(40);
            Check(offset.X < 0 && forward > 0, "reverse flow reverses texture travel");

            feedback.Flowing = false;
            Pump(80);
            Check(!pipe.HasAnimatedProperties && group.Children.Count == 4 && Difference(still, Render(scene)) == 0,
                "stopping clears the colored core and highlights and restores the original pipe pixels");

            double frozenPhase = offset.X;
            Pump(80);
            Check(offset.X == frozenPhase, "stopped flow retains its visual phase");

            feedback.Flowing = true;
            Pump(80);
            Check(pipe.IsFilled && group.Children.Count == 5 && Difference(still, Render(scene)) > 100,
                "restarting restores the colored flow without changing filled state");

            pipe.IsFilled = false;
            Check(group.Children.Count == 4 && Difference(still, Render(scene)) == 0,
                "empty pipe removes water and exposes the same hollow walls");
            pipe.IsFilled = true;

            pipe.WallOpacity = 0.85;
            var opaque = Render(scene);
            pipe.WallOpacity = 0.08;
            Check(Difference(opaque, Render(scene)) > 100, "wall opacity changes rendered pipe independently of liquid");
            pipe.WallOpacity = 0.18;

            CheckBoundAppearance(scene, feedback, pipe, group);
            CheckPaths(feedback, pipe, group);
            CheckLifecycle(scene, viewport, feedback, pipe);

            foreach (double invalidValue in new[] { double.NaN, double.PositiveInfinity, -0.1, 1.1 })
            {
                Reject(pipe, FluidRouteVisual3D.WallOpacityProperty, invalidValue);
            }
            Reject(pipe, FluidRouteVisual3D.PipeRadiusProperty, 0);
            Reject(pipe, FluidRouteVisual3D.FlowSpeedProperty, -1);

            Check(BindingOperations.IsDataBound(pipe, FluidRouteVisual3D.IsFlowingProperty)
                && BindingOperations.IsDataBound(pipe, FluidRouteVisual3D.PathProperty)
                && BindingOperations.IsDataBound(pipe, FluidRouteVisual3D.PipeRadiusProperty)
                && BindingOperations.IsDataBound(pipe, FluidRouteVisual3D.FlowColorProperty),
                "external bindings survive edits and lifecycle changes");

            if (args.Length > 0)
            {
                string output = Path.GetFullPath(args[0]);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(Render(scene)));
                using var stream = File.Create(output);
                encoder.Save(stream);
            }

            feedback.Flowing = false;
            host.RootVisual = null;
            Console.WriteLine($"PASS: {_checks} route, fluid, transparency, binding and lifecycle checks.");
            app.Shutdown();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void CheckBoundAppearance(Grid scene, Feedback feedback, FluidRouteVisual3D pipe, Model3DGroup group)
    {
        pipe.IsAnimationEnabled = false;
        Pump(50);
        var paused = Render(scene);
        Pump(80);
        Check(Difference(paused, Render(scene)) == 0 && group.Children.Count == 5 && pipe.IsFlowing,
            "pausing freezes rendered water without removing the colored core");

        Geometry3D originalGeometry = ((GeometryModel3D)group.Children[0]).Geometry;
        feedback.WaterColor = Color.FromRgb(255, 196, 102);
        Pump(40);
        Check(pipe.FlowColor == feedback.WaterColor && Difference(paused, Render(scene)) > 100,
            "changing the bound flow color updates the visible water");
        Check(ReferenceEquals(originalGeometry, ((GeometryModel3D)group.Children[0]).Geometry),
            "changing color reuses pipe geometry");
        feedback.WaterColor = Color.FromRgb(82, 212, 239);

        double originalHeight = group.Bounds.SizeY;
        feedback.Radius = 0.16;
        Pump(40);
        Check(pipe.PipeRadius == 0.16 && group.Bounds.SizeY > originalHeight,
            "changing the bound radius rebuilds the pipe at the requested size");
        feedback.Radius = 0.10;
        Pump(40);
        pipe.IsAnimationEnabled = true;
    }

    private static void CheckPaths(Feedback feedback, FluidRouteVisual3D pipe, Model3DGroup group)
    {
        Geometry3D originalGeometry = ((GeometryModel3D)group.Children[0]).Geometry;
        feedback.Route[1] = new Point3D(0.5, 2.4, 0.25);
        Pump(40);
        Check(!ReferenceEquals(originalGeometry, ((GeometryModel3D)group.Children[0]).Geometry),
            "editing the bound path collection rebuilds geometry");

        feedback.Route.Add(feedback.Route[feedback.Route.Count - 1]);
        Pump(40);
        Check(HasFiniteGeometry(group), "duplicate path points are safe");

        Point3DCollection oldPath = feedback.Route;
        feedback.Route = Point3DCollection.Parse("0,0,0 0,2,0 1,2,1 1,0,2");
        Pump(40);
        Check(ReferenceEquals(pipe.Path, feedback.Route) && HasFiniteGeometry(group) && group.Bounds.SizeZ > 1.9,
            "replacing the bound path creates vertical and spatial bends");

        Geometry3D replacementGeometry = ((GeometryModel3D)group.Children[0]).Geometry;
        oldPath[0] = new Point3D(10, 10, 10);
        Pump(40);
        Check(ReferenceEquals(replacementGeometry, ((GeometryModel3D)group.Children[0]).Geometry),
            "editing the old path no longer changes the control");

        feedback.Route.Freeze();
        feedback.Route = Feedback.CreateDefaultRoute();
        Pump(40);
        Check(HasFiniteGeometry(group) && group.Children.Count == 5, "a path frozen after binding can be replaced");

        Point3DCollection frozenPath = Feedback.CreateDefaultRoute();
        frozenPath.Freeze();
        feedback.Route = frozenPath;
        Pump(40);
        Check(ReferenceEquals(pipe.Path, frozenPath) && HasFiniteGeometry(group), "an already frozen path can be bound");

        feedback.Route = Point3DCollection.Parse("0,0,0 1,0,0 0,0,0");
        Pump(40);
        Check(HasFiniteGeometry(group), "reversing path stays finite");

        feedback.Route = new Point3DCollection
        {
            new Point3D(0, 0, 0),
            new Point3D(double.NaN, 0, 0)
        };
        Pump(40);
        Check(group.Children.Count == 0, "non-finite paths display no invalid geometry");

        feedback.Route = new Point3DCollection
        {
            new Point3D(0, 0, 0)
        };
        Pump(40);
        Check(group.Children.Count == 0 && !pipe.HasAnimatedProperties, "one point has no tube");

        feedback.Route = Feedback.CreateDefaultRoute();
        Pump(40);
        pipe.ReverseFlow = false;
    }

    private static void CheckLifecycle(Grid scene, Viewport3D viewport, Feedback feedback, FluidRouteVisual3D pipe)
    {
        feedback.Flowing = true;
        Pump(80);
        scene.Visibility = Visibility.Hidden;
        Pump(60);
        Check(!pipe.HasAnimatedProperties && pipe.IsFlowing, "hidden page stops clock without changing device state");

        scene.Visibility = Visibility.Visible;
        Pump(80);
        Check(pipe.HasAnimatedProperties, "showing page restarts flow");

        viewport.Children.Remove(pipe);
        Pump(60);
        Check(!pipe.HasAnimatedProperties, "detaching route stops clock");
        viewport.Children.Add(pipe);
        Pump(80);
        Check(pipe.HasAnimatedProperties, "reattaching route resumes flow");

        var secondPipe = new FluidRouteVisual3D
        {
            Path = Point3DCollection.Parse("0,0,1 2,0,1"),
            IsFlowing = true
        };
        viewport.Children.Add(secondPipe);
        feedback.Flowing = false;
        Pump(60);
        Check(!pipe.HasAnimatedProperties && secondPipe.HasAnimatedProperties && secondPipe.IsFlowing,
            "stopping one pipe does not stop another pipe");
        viewport.Children.Remove(secondPipe);
        feedback.Flowing = true;

        // 与实际腔体一样，把管路先装在 Arm 上，再整体挂入场景。
        viewport.Children.Remove(pipe);
        var arm = new ArmVisual3D();
        arm.Attachments.Add(pipe);
        viewport.Children.Add(arm);
        Pump(60);
        Check(pipe.HasAnimatedProperties && pipe.IsFlowing, "pipe mounted on Arm starts its flow");
        viewport.Children.Remove(arm);
        Pump(60);
        Check(!pipe.HasAnimatedProperties, "removing the whole Arm stops its attached pipe");
        viewport.Children.Add(arm);
        Pump(60);
        Check(pipe.HasAnimatedProperties, "reattaching the whole Arm resumes its attached pipe");

        viewport.Children.Remove(arm);
        arm.Attachments.Remove(pipe);
        viewport.Children.Add(pipe);

        pipe.IsAnimationEnabled = false;
        Check(!pipe.HasAnimatedProperties && pipe.IsFlowing, "animation pause retains flow state");
        pipe.IsAnimationEnabled = true;
        pipe.FlowSpeed = 0;
        Check(!pipe.HasAnimatedProperties, "zero speed stops clock");
        pipe.FlowSpeed = 0.55;
        Pump(60);
    }

    private static TranslateTransform FlowOffset(FluidRouteVisual3D pipe)
    {
        var group = (Model3DGroup)pipe.Content;
        var water = (GeometryModel3D)group.Children[2];
        var material = (MaterialGroup)water.Material;
        var highlight = (EmissiveMaterial)material.Children[1];
        var brush = (DrawingBrush)highlight.Brush;
        return (TranslateTransform)brush.Transform;
    }

    private static bool HasFiniteGeometry(Model3DGroup group)
    {
        foreach (GeometryModel3D model in group.Children)
        {
            var mesh = (MeshGeometry3D)model.Geometry;
            foreach (Point3D point in mesh.Positions)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void Layout(Grid scene)
    {
        scene.Measure(new Size(900, 450));
        scene.Arrange(new Rect(0, 0, 900, 450));
        scene.UpdateLayout();
    }

    private static RenderTargetBitmap Render(Grid scene)
    {
        Layout(scene);
        var bitmap = new RenderTargetBitmap(900, 450, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        return bitmap;
    }

    private static int Difference(BitmapSource first, BitmapSource second)
    {
        byte[] firstPixels = new byte[first.PixelWidth * first.PixelHeight * 4];
        byte[] secondPixels = new byte[firstPixels.Length];
        first.CopyPixels(firstPixels, first.PixelWidth * 4, 0);
        second.CopyPixels(secondPixels, second.PixelWidth * 4, 0);

        int changed = 0;
        for (int index = 0; index < firstPixels.Length; index++)
        {
            if (Math.Abs(firstPixels[index] - secondPixels[index]) > 8)
            {
                changed++;
            }
        }

        return changed;
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

        throw new InvalidOperationException("Invalid " + property.Name);
    }
}

// 测试用反馈源，只提供可绑定数据，不连接设备。
internal sealed class Feedback : DependencyObject
{
    public Feedback()
    {
        SetCurrentValue(RouteProperty, CreateDefaultRoute());
    }

    public bool Flowing
    {
        get
        {
            return (bool)GetValue(FlowingProperty);
        }
        set
        {
            SetValue(FlowingProperty, value);
        }
    }

    public static readonly DependencyProperty FlowingProperty = DependencyProperty.Register(
        nameof(Flowing), typeof(bool), typeof(Feedback), new PropertyMetadata(false));

    public Point3DCollection Route
    {
        get
        {
            return (Point3DCollection)GetValue(RouteProperty);
        }
        set
        {
            SetValue(RouteProperty, value);
        }
    }

    public static readonly DependencyProperty RouteProperty = DependencyProperty.Register(
        nameof(Route), typeof(Point3DCollection), typeof(Feedback), new PropertyMetadata(null));

    public Color WaterColor
    {
        get
        {
            return (Color)GetValue(WaterColorProperty);
        }
        set
        {
            SetValue(WaterColorProperty, value);
        }
    }

    public static readonly DependencyProperty WaterColorProperty = DependencyProperty.Register(
        nameof(WaterColor), typeof(Color), typeof(Feedback), new PropertyMetadata(Color.FromRgb(82, 212, 239)));

    public double Radius
    {
        get
        {
            return (double)GetValue(RadiusProperty);
        }
        set
        {
            SetValue(RadiusProperty, value);
        }
    }

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(Feedback), new PropertyMetadata(0.10d));

    public static Point3DCollection CreateDefaultRoute()
    {
        return Point3DCollection.Parse("-2.5,1.9,0 -0.5,1.9,0 -0.5,0.45,0 2.5,0.45,0");
    }
}
