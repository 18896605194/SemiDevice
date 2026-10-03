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
    private const double LevelHeight = 0.275;
    private const double Stroke = 0.55;
    private const double Chamfer = 0.015;

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

            var scene = (Grid)Application.LoadComponent(new Uri("/BowlVisual3DSmoke;component/Scene.xaml", UriKind.Relative));
            var feedback = new BowlFeedback();
            scene.DataContext = feedback;
            using var host = new HwndSource(new HwndSourceParameters("Bowl smoke")
            {
                WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                PositionX = -32000, PositionY = -32000, Width = 400, Height = 340
            });
            host.RootVisual = scene;
            Pump(60);
            var bowl = (BowlVisual3D)scene.FindName("Bowl");
            var viewport = (Viewport3D)scene.FindName("Viewport");
            var model = (Model3DGroup)bowl.Content;
            GeometryModel3D Side() => (GeometryModel3D)model.Children[1];
            Color ColorOf() => ((SolidColorBrush)((DiffuseMaterial)Side().Material).Brush).Color;
            double Bottom() => model.Bounds.Y;
            double Top() => model.Bounds.Y + model.Bounds.SizeY;
            // 上沿比这一级的原高度多出来多少：0 = 下位，Stroke = 上位。
            double Rise() => Top() - LevelHeight * bowl.HeightLevel;
            // 内外两条直侧壁始终从底部倒角连到上沿倒角，中间不露缝。
            bool WallsConnected()
            {
                var outer = model.Children[1].Bounds;
                var inner = model.Children[5].Bounds;
                return Near(outer.Y, Chamfer) && Near(inner.Y, Chamfer)
                    && Near(outer.Y + outer.SizeY, Top() - Chamfer) && Near(inner.Y + inner.SizeY, Top() - Chamfer);
            }

            var placement = new TranslateTransform3D(0, 0, 0);
            bowl.Transform = placement;
            var previews = new List<BitmapSource>();
            double previousHeight = 0;
            for (int level = 1; level <= 3; level++)
            {
                feedback.HeightLevel = level;
                Pump(30);
                Check(bowl.HeightLevel == level && model.Bounds.SizeY > previousHeight, "bound levels strictly increase wall height");
                previousHeight = model.Bounds.SizeY;
                Check(Near(model.Bounds.SizeX, 3.18) && Near(Bottom(), 0), "levels preserve width and bottom mounting plane");
                foreach (var part in model.Children.Cast<GeometryModel3D>())
                {
                    var mesh = (MeshGeometry3D)part.Geometry;
                    Check(mesh.IsFrozen && mesh.Positions.All(p => Math.Sqrt(p.X * p.X + p.Z * p.Z) >= 1.499), "thin ring stays hollow, with frozen geometry");
                    for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
                    {
                        int a = mesh.TriangleIndices[i], b = mesh.TriangleIndices[i + 1], c = mesh.TriangleIndices[i + 2];
                        var cross = Vector3D.CrossProduct(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                        Check(Vector3D.DotProduct(cross, mesh.Normals[a]) > 0, "ring face winding agrees with exterior normals");
                    }
                }
                previews.Add(Render(scene));
            }
            var meshes = model.Children.Cast<GeometryModel3D>().Select(p => p.Geometry).ToArray();
            Color idle = ColorOf();
            feedback.IsRaised = true;
            Pump(120);
            Check(bowl.HasAnimatedProperties && Rise() > 0 && Rise() < Stroke && Near(Bottom(), 0) && ColorOf() != idle,
                "boolean rise animates the rim up, keeps the bottom on the plate and highlights automatically");
            Check(WallsConnected(), "side walls stay connected to the bottom while rising");
            double intermediate = Rise();
            feedback.IsRaised = false;
            Check(Near(Rise(), intermediate), "reversal does not jump");
            Pump(550);
            Check(Near(Rise(), 0) && Near(Bottom(), 0) && !bowl.HasAnimatedProperties && ColorOf() == idle,
                "reverse returns to lower stop and removes clock/highlight");
            feedback.IsRaised = true;
            Pump(550);
            Check(Near(Rise(), Stroke) && Near(Bottom(), 0) && bowl.HeightLevel == 3,
                "raising lifts the rim by the stroke, bottom stays on the mounting plane, height level unchanged");
            Check(WallsConnected(), "raised side walls reach from the bottom chamfer to the rim");
            Check(model.Children.Cast<GeometryModel3D>().Select((p, i) => ReferenceEquals(p.Geometry, meshes[i])).All(x => x), "movement reuses meshes");
            feedback.HeightLevel = 1;
            Check(Near(Rise(), Stroke) && Near(Bottom(), 0) && WallsConnected() && bowl.IsRaised && ReferenceEquals(bowl.Transform, placement),
                "height updates preserve raised state, continuous walls and external transform");
            bowl.IsSelected = true;
            feedback.IsRaised = false;
            Pump(550);
            Check(ColorOf() != idle, "selection survives motion completion");
            bowl.IsSelected = false;
            Check(ColorOf() == idle, "deselect restores material");
            feedback.IsRaised = true;
            Pump(90);
            scene.Visibility = Visibility.Hidden;
            Pump(30);
            Check(!bowl.HasAnimatedProperties && Near(Rise(), Stroke), "hidden page settles feedback and stops animation");
            scene.Visibility = Visibility.Visible;
            feedback.IsRaised = false;
            Pump(70);
            viewport.Children.Remove(bowl);
            Check(!bowl.HasAnimatedProperties && Near(Rise(), 0), "detach stops the animation");
            viewport.Children.Add(bowl);
            Check(BindingOperations.IsDataBound(bowl, BowlVisual3D.IsRaisedProperty) && BindingOperations.IsDataBound(bowl, BowlVisual3D.HeightLevelProperty), "feedback bindings are retained");
            foreach (int invalid in new[] { -1, 0, 4 })
            {
                bool rejected = false;
                try
                {
                    new BowlVisual3D().HeightLevel = invalid;
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }

                Check(rejected, "out-of-range level rejected");
            }
            var other = new BowlVisual3D { HeightLevel = 2, IsRaised = true, Radius = 1.8 };
            Check(Near(other.Content.Bounds.Y, 0) && Near(other.Content.Bounds.SizeY, LevelHeight * 2 + Stroke)
                && Near(other.Content.Bounds.SizeX, 3.6) && !bowl.IsRaised, "initial upper state and separate Bowl instances are independent");
            if (args.Length > 0)
            {
                var drawing = new DrawingVisual();
                using (var dc = drawing.RenderOpen())
                {
                    for (int i = 0; i < previews.Count; i++)
                    {
                        dc.DrawImage(previews[i], new Rect(i * 402, 0, 400, 340));
                    }
                }

                var bitmap = new RenderTargetBitmap(1204, 340, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(drawing);
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
            Console.WriteLine("PASS: Bowl three levels, hollow geometry, bindings, fixed-bottom rise/fall with continuous walls, reversal, highlight, visibility and detach lifecycle.");
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
        scene.Measure(new Size(400, 340));
        scene.Arrange(new Rect(0, 0, 400, 340));
        scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap(400, 340, 96, 96, PixelFormats.Pbgra32);
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

internal sealed class BowlFeedback : DependencyObject
{
    public int HeightLevel
    {
        get => (int)GetValue(HeightLevelProperty);
        set => SetValue(HeightLevelProperty, value);
    }
    public static readonly DependencyProperty HeightLevelProperty = DependencyProperty.Register(
        nameof(HeightLevel), typeof(int), typeof(BowlFeedback), new PropertyMetadata(1));
    public bool IsRaised
    {
        get => (bool)GetValue(IsRaisedProperty);
        set => SetValue(IsRaisedProperty, value);
    }
    public static readonly DependencyProperty IsRaisedProperty = DependencyProperty.Register(
        nameof(IsRaised), typeof(bool), typeof(BowlFeedback), new PropertyMetadata(false));
}
