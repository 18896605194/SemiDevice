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

            var scene = (Grid)Application.LoadComponent(new Uri("/DoorVisual3DSmoke;component/Scene.xaml", UriKind.Relative));
            var feedback = new DoorFeedback();
            scene.DataContext = feedback;
            using var host = new HwndSource(new HwndSourceParameters("Door smoke")
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                PositionX = -32000, PositionY = -32000, Width = 580, Height = 410
            });
            host.RootVisual = scene;
            Pump(70);
            var door = (DoorVisual3D)scene.FindName("Door");
            var plate = (ChamberBaseVisual3D)scene.FindName("Base");
            var root = (Viewport3D)scene.FindName("Viewport");
            var lift = (LiftVisual3D)scene.FindName("Lift");
            var model = (Model3DGroup)door.Content;
            var parts = model.Children.Cast<GeometryModel3D>().ToArray();
            var meshes = parts.Select(p => p.Geometry).ToArray();
            var fixedBounds = parts.Skip(1).Select(p => p.Bounds).ToArray();
            Check(model.Bounds.Y >= 0 && model.Bounds.X >= -door.Width / 2 - 0.12 - 1e-7
                && model.Bounds.X + model.Bounds.SizeX <= door.Width / 2 + 0.12 + 1e-7,
                "door has no actuator or connector protruding below or outside the frame");
            var placement = door.Transform;
            double Y() => parts[0].Bounds.Y;
            Color ColorOf() => ((SolidColorBrush)((DiffuseMaterial)parts[0].Material).Brush).Color;
            Color idle = ColorOf();
            double closedY = Y();
            var closed = Render(scene);
            feedback.IsOpen = true;
            Pump(130);
            Check(door.HasAnimatedProperties && Y() > closedY && ColorOf() != idle, "opening raises panel and highlights automatically");
            ((TextBlock)scene.FindName("StateTitle")).Text = "Door · 上升打开中";
            var moving = Render(scene);
            double halfway = Y();
            feedback.IsOpen = false;
            Check(Math.Abs(Y() - halfway) < 0.000001, "reversal starts from current position");
            Pump(550);
            Check(Near(Y(), closedY) && !door.HasAnimatedProperties && ColorOf() == idle, "closing completes and removes clock/highlight");
            feedback.IsOpen = true;
            Pump(550);
            double frameTop = parts[3].Bounds.Y + parts[3].Bounds.SizeY;
            double openY = Y();
            Check(Y() > frameTop && !door.HasAnimatedProperties, "open panel bottom clears the entire frame, including the top beam");
            Check(parts[0].Bounds.Z > parts[3].Bounds.Z + parts[3].Bounds.SizeZ, "panel slides in front of the fixed crossbeam without intersecting it");
            Check(parts.Skip(1).Select((p, i) => p.Bounds == fixedBounds[i]).All(x => x), "frame stays fixed");
            Check(parts.Select((p, i) => ReferenceEquals(p.Geometry, meshes[i]) && p.Geometry.IsFrozen).All(x => x), "animation reuses frozen geometry");
            Check(!lift.IsRaised && ReferenceEquals(door.Transform, placement), "door motion preserves Lift state and installation");
            ((TextBlock)scene.FindName("StateTitle")).Text = "Door · 上位打开，门洞空出";
            var opened = Render(scene);
            door.IsSelected = true;
            feedback.IsOpen = false;
            Pump(550);
            Check(ColorOf() != idle, "selection survives completion");
            door.IsSelected = false;
            Check(ColorOf() == idle, "deselection restores idle");

            // 未知（命令发了、到位信号还没亮）：门板停在行程中间、一直高亮；到位后走到那一头、高亮撤掉
            door.IsUnknown = true;
            Pump(550);
            Check(Math.Abs(Y() - (closedY + openY) / 2) < 0.000001 && !door.HasAnimatedProperties && ColorOf() != idle,
                "unknown parks the panel mid-stroke and keeps the highlight after the transition");
            door.IsUnknown = false;
            Pump(550);
            Check(Near(Y(), closedY) && ColorOf() == idle, "known again: the panel finishes at the reported end and drops the highlight");
            var initialUnknown = new DoorVisual3D { Width = door.Width, Height = door.Height, IsUnknown = true };
            var initialClosed = new DoorVisual3D { Width = door.Width, Height = door.Height };
            double initialTravel = ((GeometryModel3D)((Model3DGroup)initialUnknown.Content).Children[0]).Bounds.Y
                - ((GeometryModel3D)((Model3DGroup)initialClosed.Content).Children[0]).Bounds.Y;
            Check(!initialUnknown.HasAnimatedProperties && Math.Abs(initialTravel - (openY - closedY) / 2) < 0.000001,
                "initial unknown is drawn mid-stroke immediately before attachment");

            feedback.IsOpen = true;
            Pump(60);
            scene.Visibility = Visibility.Hidden;
            Pump(30);
            Check(!door.HasAnimatedProperties && Y() > frameTop, "hidden scene settles open feedback and stops clock");
            scene.Visibility = Visibility.Visible;
            feedback.IsOpen = false;
            Pump(60);
            root.Children.Remove(plate);
            Check(!door.HasAnimatedProperties && Near(Y(), closedY), "removing whole base stops door animation");
            root.Children.Add(plate);
            Check(BindingOperations.IsDataBound(door, DoorVisual3D.IsOpenProperty), "boolean feedback binding survives transitions");
            var initial = new DoorVisual3D { IsOpen = true };
            Check(!initial.HasAnimatedProperties && ((GeometryModel3D)((Model3DGroup)initial.Content).Children[0]).Bounds.Y > frameTop, "initial open state is immediate before attachment");
            if (args.Length > 0)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(closed, new Rect(0, 0, 580, 410));
                    dc.DrawImage(moving, new Rect(582, 0, 580, 410));
                    dc.DrawImage(opened, new Rect(1164, 0, 580, 410));
                }
                var bitmap = new RenderTargetBitmap(1744, 410, 96, 96, PixelFormats.Pbgra32);
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
            Console.WriteLine("PASS: Door boolean binding, open/close, reversal, moving highlight, unknown parked mid-stroke, fixed frame, assembly independence and visibility/detach lifecycle.");
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

internal sealed class DoorFeedback : DependencyObject
{
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(DoorFeedback), new PropertyMetadata(false));
}
