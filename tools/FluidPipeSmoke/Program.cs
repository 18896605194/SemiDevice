using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using xyz.Client.Presentation.Controls;
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
            var scene = (Canvas)Application.LoadComponent(new Uri("/FluidPipeSmoke;component/Scene.xaml", UriKind.Relative));
            var feedback = new Feedback();
            scene.DataContext = feedback;

            using var host = new HwndSource(new HwndSourceParameters("FluidPipeSmoke")
            {
                Width = 760,
                Height = 380,
                PositionX = -32000,
                PositionY = -32000,
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08000080
            });
            host.RootVisual = scene;
            Layout(scene);
            Pump(120);

            var straight = (FluidPipe)scene.FindName("Straight");
            var bend = (FluidPipe)scene.FindName("Bend");
            var vertical = (FluidPipe)scene.FindName("Vertical");
            FluidRouteVisual3D straightModel = ModelOf(straight);
            FluidRouteVisual3D bendModel = ModelOf(bend);
            FluidRouteVisual3D verticalModel = ModelOf(vertical);

            Check(straight.IsLoaded && straight.ActualWidth == 240 && straight.ActualHeight >= 14,
                "one control tag with Width gets a visible automatic height");
            Check(straightModel.Path!.Count == 2 && straightModel.Path[1].X > straightModel.Path[0].X,
                "omitting Points creates a horizontal pipe");
            Check(vertical.ActualHeight == 280 && vertical.ActualWidth >= 14
                && verticalModel.Path![1].Y < verticalModel.Path[0].Y,
                "Orientation and Height create a vertical pipe without Points");
            Check(new FluidPipe().Points != new FluidPipe().Points, "controls do not share mutable point collections");
            Check(ReferenceEquals(straight.DataContext, feedback) && ReferenceEquals(bend.DataContext, feedback),
                "ordinary DataContext inheritance works without an ElementName binding");
            Check(bendModel.Path!.Count == 4 && bendModel.Path[2].Y < 0 && bend.ActualWidth > 340,
                "2D points use screen coordinates and determine natural size");

            BitmapSource stopped = Render(straight);
            Check(VisiblePixelCount(stopped) > 100, "a stopped pipe is actually rendered");
            Check(HasClearEdges(Render(bend)), "automatic camera keeps a bent pipe inside its control bounds");

            feedback.Flowing = true;
            Pump(100);
            BitmapSource first = Render(straight);
            Pump(180);
            Check(straightModel.HasAnimatedProperties && Difference(first, Render(straight)) > 20,
                "a normal bool binding starts visible flowing water");

            feedback.Flowing = false;
            Pump(80);
            Check(!straightModel.HasAnimatedProperties && Layers(straightModel) == 4
                && Difference(stopped, Render(straight)) == 0,
                "stop restores exactly the empty transparent pipe");
            Check(verticalModel.HasAnimatedProperties && vertical.IsFlowing,
                "stopping bound controls does not stop an independent control");

            feedback.Flowing = true;
            Pump(80);
            bend.IsAnimationEnabled = false;
            Pump(40);
            BitmapSource paused = Render(bend);
            Pump(100);
            Check(!bendModel.HasAnimatedProperties && Layers(bendModel) == 5
                && Difference(paused, Render(bend)) == 0,
                "animation pause keeps the colored core and freezes its pixels");

            feedback.WaterColor = Color.FromRgb(255, 196, 102);
            Pump(40);
            Check(bendModel.FlowColor == feedback.WaterColor && Difference(paused, Render(bend)) > 100,
                "ordinary color binding updates visible water");
            bend.IsAnimationEnabled = true;
            bend.ReverseFlow = true;
            bend.FlowSpeed = 90;
            Check(bendModel.ReverseFlow && Math.Abs(bendModel.FlowSpeed - 0.9) < 0.00001,
                "flow settings reach the internal renderer");

            double originalLength = straightModel.Path![1].X;
            double originalRadius = straightModel.PipeRadius;
            straight.Width = 420;
            Layout(scene);
            Pump(50);
            Check(straightModel.Path[1].X > originalLength && straightModel.PipeRadius == originalRadius,
                "resizing a straight pipe changes length without changing thickness");

            var style = new Style(typeof(FluidPipe));
            style.Setters.Add(new Setter(FluidPipe.PipeThicknessProperty, 20d));
            style.Setters.Add(new Setter(FluidPipe.FlowColorProperty, Colors.CornflowerBlue));
            vertical.Style = style;
            Layout(scene);
            Pump(50);
            Check(verticalModel.PipeRadius == 0.1 && verticalModel.FlowColor == Colors.CornflowerBlue,
                "ordinary WPF Style setters configure the control");

            double originalHeight = bend.ActualHeight;
            feedback.Route[2] = new Point(180, 170);
            Layout(scene);
            Pump(50);
            Check(bendModel.Path![2].Y == -1.7 && bend.ActualHeight > originalHeight,
                "editing a bound point updates geometry and desired size");

            PointCollection previousPoints = feedback.Route;
            feedback.Route = PointCollection.Parse("0,0 100,0 100,80");
            Layout(scene);
            Pump(50);
            Check(bendModel.Path!.Count == 3 && bendModel.Path[2].Y == -0.8,
                "replacing a bound point collection updates the route");
            previousPoints[0] = new Point(999, 999);
            Check(bendModel.Path[0].X == 0, "old point collections no longer change the control");

            feedback.Route.Freeze();
            var frozenPoints = PointCollection.Parse("0,0 120,0 120,90");
            frozenPoints.Freeze();
            feedback.Route = frozenPoints;
            Layout(scene);
            Pump(50);
            Check(bendModel.Path![2].Y == -0.9, "frozen paths can be replaced and bound");

            feedback.Route = new PointCollection
            {
                new Point(0, 0),
                new Point(double.NaN, 10)
            };
            Layout(scene);
            Check(Layers(bendModel) == 0, "invalid points produce an empty view");
            feedback.Route = new PointCollection
            {
                new Point(10, 10)
            };
            Layout(scene);
            Check(Layers(bendModel) == 0, "one point does not produce a stray pipe");

            feedback.Route = Feedback.CreateRoute();
            Layout(scene);
            Pump(80);
            scene.Visibility = Visibility.Hidden;
            Pump(60);
            Check(!straightModel.HasAnimatedProperties && !bendModel.HasAnimatedProperties
                && straight.IsFlowing, "hiding the page stops animation without changing feedback");
            scene.Visibility = Visibility.Visible;
            Pump(80);
            Check(straightModel.HasAnimatedProperties && bendModel.HasAnimatedProperties,
                "showing the page resumes animation");

            scene.Children.Remove(bend);
            Pump(80);
            Check(!bend.IsLoaded && !bendModel.HasAnimatedProperties && feedback.Flowing,
                "removing the regular control stops its internal animation");
            scene.Children.Add(bend);
            Layout(scene);
            Pump(100);
            Check(bend.IsLoaded && bendModel.HasAnimatedProperties, "readding the regular control resumes animation");

            var replacement = new Feedback
            {
                Flowing = false
            };
            scene.DataContext = replacement;
            Layout(scene);
            Pump(60);
            Check(!straight.IsFlowing && !bend.IsFlowing && ReferenceEquals(bend.Points, replacement.Route),
                "changing the page DataContext switches every ordinary binding");
            Check(BindingOperations.IsDataBound(straight, FluidPipe.IsFlowingProperty)
                && BindingOperations.IsDataBound(bend, FluidPipe.PointsProperty)
                && BindingOperations.IsDataBound(bend, FluidPipe.FlowColorProperty),
                "bindings remain attached after updates and unload");

            Reject(straight, FluidPipe.PipeThicknessProperty, 0d);
            Reject(straight, FluidPipe.FlowSpeedProperty, -1d);
            Reject(straight, FluidPipe.WallOpacityProperty, double.NaN);
            Reject(straight, FluidPipe.OrientationProperty, (Orientation)99);

            replacement.Flowing = true;
            straight.Width = 240;
            bend.ReverseFlow = false;
            Layout(scene);
            Pump(150);
            if (args.Length > 0)
            {
                Save(Render(scene), args[0]);
            }

            host.RootVisual = null;
            Pump(80);
            app.Shutdown();
            Console.WriteLine($"PASS: {_checks} regular-control, binding, layout and rendering checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static FluidRouteVisual3D ModelOf(FluidPipe pipe)
    {
        return (FluidRouteVisual3D)pipe.FindName("PipeModel");
    }

    private static int Layers(FluidRouteVisual3D pipe)
    {
        return ((Model3DGroup)pipe.Content).Children.Count;
    }

    private static void Layout(FrameworkElement scene)
    {
        scene.Measure(new Size(760, 380));
        scene.Arrange(new Rect(0, 0, 760, 380));
        scene.UpdateLayout();
    }

    private static BitmapSource Render(FrameworkElement element)
    {
        element.UpdateLayout();
        var root = (FrameworkElement)PresentationSource.FromVisual(element).RootVisual;
        int width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        if (ReferenceEquals(root, element))
        {
            return bitmap;
        }

        // 从实际页面像素裁出控件，保留 Canvas 布局坐标下的真实渲染结果。
        Point origin = element.TransformToAncestor(root).Transform(new Point(0, 0));
        var bounds = new Int32Rect((int)origin.X, (int)origin.Y,
            (int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight));
        return new CroppedBitmap(bitmap, bounds);
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    private static int VisiblePixelCount(BitmapSource bitmap)
    {
        byte[] pixels = Pixels(bitmap);
        int visible = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (!IsBackground(pixels, index - 3))
            {
                visible++;
            }
        }

        return visible;
    }

    private static bool IsBackground(byte[] pixels, int offset)
    {
        return Math.Abs(pixels[offset] - pixels[0]) <= 4
            && Math.Abs(pixels[offset + 1] - pixels[1]) <= 4
            && Math.Abs(pixels[offset + 2] - pixels[2]) <= 4;
    }
    private static bool HasClearEdges(BitmapSource bitmap)
    {
        byte[] pixels = Pixels(bitmap);
        for (int x = 0; x < bitmap.PixelWidth; x++)
        {
            if (!IsBackground(pixels, x * 4) || !IsBackground(pixels, ((bitmap.PixelHeight - 1) * bitmap.PixelWidth + x) * 4))
            {
                return false;
            }
        }

        for (int y = 0; y < bitmap.PixelHeight; y++)
        {
            if (!IsBackground(pixels, (y * bitmap.PixelWidth) * 4) || !IsBackground(pixels, (y * bitmap.PixelWidth + bitmap.PixelWidth - 1) * 4))
            {
                return false;
            }
        }

        return VisiblePixelCount(bitmap) > 100;
    }

    private static int Difference(BitmapSource first, BitmapSource second)
    {
        byte[] before = Pixels(first);
        byte[] after = Pixels(second);
        if (before.Length != after.Length)
        {
            throw new InvalidOperationException("Images must have the same size.");
        }

        int changed = 0;
        for (int index = 0; index < before.Length; index++)
        {
            if (Math.Abs(before[index] - after[index]) > 8)
            {
                changed++;
            }
        }

        return changed;
    }

    private static void Save(BitmapSource bitmap, string fileName)
    {
        string path = Path.GetFullPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
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

    private static void Reject(DependencyObject target, DependencyProperty property, object value)
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

// 测试用可绑定数据源，不连接设备。
internal sealed class Feedback : DependencyObject
{
    public Feedback()
    {
        SetCurrentValue(RouteProperty, CreateRoute());
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

    public PointCollection Route
    {
        get
        {
            return (PointCollection)GetValue(RouteProperty);
        }
        set
        {
            SetValue(RouteProperty, value);
        }
    }

    public static readonly DependencyProperty RouteProperty = DependencyProperty.Register(
        nameof(Route), typeof(PointCollection), typeof(Feedback), new PropertyMetadata(null));

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

    public static PointCollection CreateRoute()
    {
        return PointCollection.Parse("0,0 180,0 180,120 340,120");
    }
}
