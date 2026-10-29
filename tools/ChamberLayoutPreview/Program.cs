using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ChamberLayoutEditor;
using Microsoft.Extensions.DependencyInjection;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Tools;

namespace ChamberLayoutPreview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
            { BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark, PrimaryColor = MaterialDesignColors.PrimaryColor.Blue, SecondaryColor = MaterialDesignColors.SecondaryColor.Lime });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml") });
            foreach (string name in new[] { "Styles/Color", "Styles/DarkColors", "Styles/FontSize", "Styles/TextBlockStyles", "Styles/BorderStyles",
                         "Styles/ButtonStyles", "Styles/TextBoxStyles", "Styles/ComboBoxStyles", "Styles/ListBoxStyles", "Localization/Strings.zh-CN" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"pack://application:,,,/xyz.Client.Presentation;component/{name}.xaml") });
            L10n.AddPack("ChamberLayoutEditor");
            using var services = new ServiceCollection().AddChamberLayoutEditor().BuildServiceProvider();
            IocHelper.ServiceProvider = services;
            var page = new ChamberEditorPage();
            foreach (var model in services.GetServices<BaseViewModel>()) model.Init();

            if (args.Length == 2 && args[0] is "--render" or "--self-test")
            {
                string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                const int width = 1460, height = 960;
                using var host = new HwndSource(new HwndSourceParameters("Chamber layout preview")
                { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080, PositionX = -32000, PositionY = -32000, Width = width, Height = height });
                page.Width = width; page.Height = height; host.RootVisual = page;
                page.Measure(new Size(width, height)); page.Arrange(new Rect(0, 0, width, height)); page.UpdateLayout(); Pump(400);
                if (args[0] == "--self-test") EditorChecks.Run(page, output);
                Capture(page, Path.Combine(output, "chamber-editor.png"));
                page.ViewModel.IsPreview = true; page.ViewModel.Reach = 1; page.ViewModel.Lift = true; page.ViewModel.Spray = true;
                Pump(500); Capture(page, Path.Combine(output, "chamber-preview.png"));
                page.ViewModel.IsPreview = false;
                app.Shutdown();
                return 0;
            }
            if (args.Length == 2 && args[0] == "--machine")
                page.LoadDocument(ChamberLayoutDocument.ReadMachine(File.ReadAllText(args[1]))[0]);
            var window = new Window { Title = L10n.Get("chambereditor.title"), Content = page, Width = 1460, Height = 960,
                MinWidth = 1100, MinHeight = 760, Background = (Brush)app.Resources["DarkWindowBackground"], WindowStartupLocation = WindowStartupLocation.CenterScreen };
            window.Closing += (_, e) => { if (!page.ConfirmClose()) e.Cancel = true; };
            app.Run(window);
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "chamber-editor-error.log"), ex.ToString());
            return 1;
        }
    }

    internal static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
