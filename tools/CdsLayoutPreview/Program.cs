using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CdsLayoutPreview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
            {
                BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark,
                PrimaryColor = MaterialDesignColors.PrimaryColor.Blue,
                SecondaryColor = MaterialDesignColors.SecondaryColor.Lime
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml")
            });
            foreach (string name in new[] { "Styles/Color", "Styles/DarkColors", "Styles/FontSize", "Styles/TextBlockStyles", "Styles/BorderStyles", "Styles/ButtonStyles", "Localization/Strings.zh-CN" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/xyz.Client.Presentation;component/{name}.xaml")
                });
            }

            var page = new PreviewPage();
            if (args.Length == 2 && args[0] is "--render" or "--self-test" or "--benchmark")
            {
                const int width = 1440;
                const int height = 960;
                using var host = new HwndSource(new HwndSourceParameters("CDS preview render")
                {
                    WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
                    PositionX = -32000, PositionY = -32000, Width = width, Height = height
                });
                page.Width = width;
                page.Height = height;
                host.RootVisual = page;
                page.Measure(new Size(width, height));
                page.Arrange(new Rect(0, 0, width, height));
                page.UpdateLayout();
                Directory.CreateDirectory(args[1]);
                if (args[0] == "--benchmark")
                {
                    Pump(300);
                    EditorBenchmark.Run(page, args[1]);
                    app.Shutdown();
                    return 0;
                }
                if (args[0] == "--self-test")
                {
                    EditorSmoke.Run(page, args[1]);
                }
                else
                {
                    page.ObjectPicker.SelectedItem = page.Document.Pipes.First();
                }
                Pump(350);
                SaveImage(page, width, height, Path.Combine(args[1], "cds-editor.png"));
                page.SetTool("preview");
                foreach (string mode in new[] { "supply", "fill", "drain", "idle" })
                {
                    System.Windows.Controls.Button button;
                    switch (mode)
                    {
                        case "fill":
                            button = page.FillButton;
                            break;
                        case "drain":
                            button = page.DrainButton;
                            break;
                        case "supply":
                            button = page.SupplyButton;
                            break;
                        default:
                            button = page.StopButton;
                            break;
                    }

                    button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Pump(350);
                    page.PauseAnimation();
                    SaveImage(page, width, height, Path.Combine(args[1], $"cds-{mode}.png"));
                }

                page.LevelPicker.SelectedIndex = 4;
                Pump(300);
                if (page.Tank.Level != xyz.Client.Presentation.Models.TankLevel.HH || page.Tank.HH.IsActive != true)
                {
                    throw new InvalidOperationException("模拟液位切换未更新槽模型和信号灯。");
                }

                page.LevelPicker.SelectedIndex = 2;
                page.SetTool("select");
                page.Width = 1100;
                page.Height = 780;
                page.Measure(new Size(1100, 780));
                page.Arrange(new Rect(0, 0, 1100, 780));
                page.UpdateLayout();
                var localPoint = new Point(500, 280);
                Point pagePoint = page.Stage.TranslatePoint(localPoint, page);
                if ((page.TranslatePoint(pagePoint, page.Stage) - localPoint).Length > 0.001)
                {
                    throw new InvalidOperationException("缩放后的画布坐标转换不一致。");
                }
                Pump(300);
                SaveImage(page, 1100, 780, Path.Combine(args[1], "cds-editor-small.png"));

                app.Shutdown();
                return 0;
            }

            var window = new Window
            {
                Title = "CDS 管路布局编辑 — 管路属性 / 模拟数据",
                Width = 1440, Height = 960, MinWidth = 1100, MinHeight = 780,
                Content = page, WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            window.Closing += (_, eventArgs) =>
            {
                eventArgs.Cancel = !page.ConfirmDiscardChanges();
            };
            return app.Run(window);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preview-error.log"), exception.ToString());
            return 1;
        }
    }

    private static void SaveImage(FrameworkElement page, int width, int height, string path)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
