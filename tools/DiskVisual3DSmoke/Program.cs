using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;

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

            var scene = (Grid)Application.LoadComponent(new Uri("/DiskVisual3DSmoke;component/Scene.xaml", UriKind.Relative));
            var feedback = new Feedback { Wafer = new WaferModel { LpSlot = "LP1-03", State = "Process" } };
            scene.DataContext = feedback;
            using var host = new HwndSource(new HwndSourceParameters("Disk smoke")
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                PositionX = -32000, PositionY = -32000, Width = 680, Height = 350
            });
            host.RootVisual = scene;
            Pump(90);
            var disk = (DiskVisual3D)scene.FindName("Disk");
            var flat = (Wafer)scene.FindName("FlatDisk");
            var viewport = (Viewport3D)scene.FindName("Viewport");
            var plate = (ChamberBaseVisual3D)scene.FindName("Base");
            var surface = disk.Children.OfType<Viewport2DVisual3D>().Single();
            var face = (Wafer)surface.Visual;
            var model = (Model3DGroup)disk.Content;
            var rotation = (AxisAngleRotation3D)((RotateTransform3D)model.Transform).Rotation;
            var faceRoot = (Grid)face.FindName("rootGrid");
            var faceText = faceRoot.Children.OfType<Viewbox>().Select(v => v.Child).OfType<TextBlock>().Single();
            Brush Fill(Wafer wafer) => ((Ellipse)wafer.FindName("diskEllipse")).Fill;
            Check(ReferenceEquals(face.Data, flat.Data) && faceText.Text == "LP1-03", "same wafer data and slot text reach both controls");
            var processPreview = Render(scene);
            foreach (string state in new[] { "IdleNojob", "IdleHasjob", "Process", "Completed", "Error", "RcmCompleted", "Soaking", "Transfer", "Crossed", "Double" })
            {
                feedback.Wafer!.State = state;
                Pump(20);
                Check(Fill(face).ToString() == Fill(flat).ToString(), $"state color matches 2D: {state}");
            }
            disk.Label = "CH1";
            Pump(20);
            Check(faceText.Text == "CH1", "explicit label is displayed");
            disk.Label = "";
            feedback.Wafer!.LpSlot = "LP2-08";
            Pump(20);
            Check(faceText.Text == "LP2-08", "empty label restores live slot binding");
            feedback.Wafer.State = "Completed";
            var completedPreview = Render(scene);
            var meshes = model.Children.Cast<GeometryModel3D>().Select(p => p.Geometry).ToArray();
            feedback.Speed = 180;
            Pump(100);
            Check(disk.HasAnimatedProperties && rotation.Angle > 0 && flat.RotationSpeed == 0, "3D clockwise spin does not change 2D rotation");
            double before = rotation.Angle;
            Pump(100);
            Check(rotation.Angle > before, "3D rotation progresses");
            disk.RotateClockwise = false;
            Check(Math.Abs(rotation.Angle - before) < 40, "reversal preserves position");
            before = rotation.Angle;
            Pump(100);
            Check(rotation.Angle < before, "reverse rotation progresses in opposite direction");
            feedback.Speed = 0;
            before = rotation.Angle;
            Pump(100);
            Check(!disk.HasAnimatedProperties && Near(rotation.Angle, before), "stop retains angle and removes clock");
            Check(model.Children.Cast<GeometryModel3D>().Select((p, i) => ReferenceEquals(p.Geometry, meshes[i])).All(x => x), "rotation does not rebuild meshes");
            feedback.Speed = 180;
            Pump(30);
            scene.Visibility = Visibility.Hidden;
            Pump(30);
            Check(!disk.HasAnimatedProperties, "hidden page stops animation");
            scene.Visibility = Visibility.Visible;
            Pump(30);
            Check(disk.HasAnimatedProperties, "visible page resumes animation");
            viewport.Children.Remove(plate);
            Check(!disk.HasAnimatedProperties, "removing base stops nested disk animation");
            viewport.Children.Add(plate);
            Pump(30);
            Check(disk.HasAnimatedProperties, "reattaching base resumes nested disk animation");
            feedback.Speed = 0;

            var create = new TestCommand();
            var delete = new TestCommand();
            var parameter = new object();
            disk.CreateCommand = create;
            disk.DeleteCommand = delete;
            disk.CommandParameter = parameter;
            Pump(20);
            var createItem = (MenuItem)face.FindName("createMenuItem");
            var deleteItem = (MenuItem)face.FindName("deleteMenuItem");
            void OpenMenuLogic() => typeof(Wafer).GetMethod("OnContextMenuOpening", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.Invoke(face, new object?[] { faceRoot, null });
            OpenMenuLogic();
            Pump(20);
            Check(createItem.Visibility == Visibility.Collapsed && deleteItem.Visibility == Visibility.Visible, "wafer present offers delete");
            Check(ReferenceEquals(deleteItem.Command, delete) && ReferenceEquals(deleteItem.CommandParameter, parameter), "delete menu binds the external command and parameter");
            disk.DeleteEnable = false;
            Pump(20);
            Check(!deleteItem.IsEnabled, "delete enable is respected");
            disk.DeleteEnable = true;
            delete.Allowed = false;
            delete.Refresh();
            Pump(20);
            Check(!deleteItem.IsEnabled, "command CanExecute is respected");
            delete.Allowed = true;
            delete.Refresh();
            Pump(20);
            typeof(MenuItem).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(deleteItem, null);
            Pump(30);
            Check(delete.Count == 1 && ReferenceEquals(delete.Parameter, parameter), "menu invokes delete command without implementing device logic");
            feedback.Wafer = null;
            Pump(20);
            OpenMenuLogic();
            Pump(20);
            Check(disk.Content is null && surface.Visual == face && createItem.Visibility == Visibility.Visible && deleteItem.Visibility == Visibility.Collapsed,
                "empty slot hides disk but retains create interaction");
            disk.CreateEnable = false;
            Pump(20);
            Check(!createItem.IsEnabled, "create enable is respected");
            disk.CreateEnable = true;
            Pump(20);
            typeof(MenuItem).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(createItem, null);
            Pump(30);
            Check(create.Count == 1 && ReferenceEquals(create.Parameter, parameter), "create command receives parameter");
            disk.CreateCommand = null;
            Pump(20);
            Check(surface.Visual is null, "empty slot with no create action is fully hidden");
            disk.IsDiskVisible = true;
            Pump(20);
            Check(ReferenceEquals(disk.Content, model) && ReferenceEquals(surface.Visual, face), "always-visible disk remains without wafer data");
            disk.FillColor = Brushes.Orange;
            Pump(20);
            Check(Fill(face).ToString() == Brushes.Orange.ToString(), "fallback fill is forwarded");
            disk.Radius = 1.5;
            disk.Thickness = 0.08;
            Check(Near(model.Children[0].Bounds.SizeX, 3), "radius updates actual 3D geometry");
            Check(BindingOperations.IsDataBound(disk, DiskVisual3D.DataProperty) && BindingOperations.IsDataBound(disk, DiskVisual3D.RotationSpeedProperty), "external bindings survive state changes");
            Check(Viewport2DVisual3D.GetIsVisualHostMaterial(surface.Material), "surface enables WPF 3D input routing");
            var hits = new List<RayHitTestResult>();
            VisualTreeHelper.HitTest(disk, null, result =>
                {
                    if (result is RayHitTestResult ray)
                    {
                        hits.Add(ray);
                    }

                    return HitTestResultBehavior.Continue;
                },
                new RayHitTestParameters(new Point3D(0, 3, 0), new Vector3D(0, -1, 0)));
            Check(hits.OrderBy(hit => hit.DistanceToRayOrigin).FirstOrDefault()?.VisualHit == surface, "ray hits interactive top surface rather than underlying geometry");
            if (args.Length > 0)
            {
                var drawing = new DrawingVisual();
                using (var dc = drawing.RenderOpen())
                {
                    dc.DrawImage(processPreview, new Rect(0, 0, 680, 350));
                    dc.DrawImage(completedPreview, new Rect(0, 352, 680, 350));
                }
                var bitmap = new RenderTargetBitmap(680, 702, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(drawing);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string output = System.IO.Path.GetFullPath(args[0]);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
                using var file = File.Create(output);
                encoder.Save(file);
                Console.WriteLine($"Preview: {output}");
            }
            host.RootVisual = null;
            app.Shutdown();
            Console.WriteLine("PASS: Disk 2D parity, state colors, labels, menu commands, CanExecute, 3D rotation/hit testing, visibility and assembly lifecycle.");
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
        scene.Measure(new Size(680, 350));
        scene.Arrange(new Rect(0, 0, 680, 350));
        scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap(680, 350, 96, 96, PixelFormats.Pbgra32);
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
internal sealed class TestCommand : ICommand
{
    public bool Allowed { get; set; } = true;
    public int Count { get; private set; }
    public object? Parameter { get; private set; }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public bool CanExecute(object? parameter) => Allowed;
    public void Execute(object? parameter) { Count++; Parameter = parameter; }
}
internal sealed class Feedback : DependencyObject
{
    public WaferModel? Wafer { get => (WaferModel?)GetValue(WaferProperty); set => SetValue(WaferProperty, value); }
    public static readonly DependencyProperty WaferProperty = DependencyProperty.Register(nameof(Wafer), typeof(WaferModel), typeof(Feedback));
    public double Speed { get => (double)GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }
    public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(nameof(Speed), typeof(double), typeof(Feedback), new PropertyMetadata(0d));
}
