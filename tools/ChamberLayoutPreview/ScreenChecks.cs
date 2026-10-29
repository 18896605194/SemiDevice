using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml.Linq;
using ChamberLayoutEditor;

namespace ChamberLayoutPreview;

internal static class ScreenChecks
{
    public static void Run(ChamberEditorPage page, HwndSource host, string output)
    {
        var results = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); results.Add("PASS " + message); }
        var scene = (ChamberLayoutScene)page.FindName("Scene");
        var model = page.ViewModel;
        string before = model.PrepareSave();
        double originalWidth = page.Width, originalHeight = page.Height;
        var originalView = scene.ViewPreset;
        var originalSelection = model.Selected;
        int originalTab = model.SelectedInspectorTab;
        var cases = new[]
        {
            (Width: 800, Height: 520, Embedded: false, Zoom: 1d),
            (Width: 1280, Height: 720, Embedded: false, Zoom: 1d),
            (Width: 1920, Height: 1080, Embedded: false, Zoom: 1d),
            (Width: 1280, Height: 1024, Embedded: false, Zoom: 1d),
            (Width: 2560, Height: 1080, Embedded: false, Zoom: 1d),
            (Width: 1280, Height: 1024, Embedded: true, Zoom: 1d),
            (Width: 2560, Height: 1080, Embedded: true, Zoom: 1d),
            (Width: 1600, Height: 900, Embedded: true, Zoom: 1.25),
            (Width: 1280, Height: 1024, Embedded: true, Zoom: 1.5)
        };

        try
        {
            Check((string?)XDocument.Parse(before).Root!.Attribute("Units") == "Logical", "布局显式保存逻辑单位，不改变数值");
            Check(model.Parts.SelectMany(p => p.Dimensions).All(d => d.Unit == xyz.Client.Presentation.Localization.L10n.Get(
                d.Key.EndsWith("Scale", StringComparison.Ordinal) ? "chambereditor.unit.scale" : "chambereditor.unit.logical")),
                "长度尺寸与倍率显示各自单位");
            var legacy = XDocument.Parse(before); legacy.Root!.Attribute("Units")!.Remove();
            Check(ChamberLayoutDocument.Parse(legacy.ToString()).ToXml() == before, "未写 Units 的旧布局仍按逻辑单位加载");
            bool rejected = false;
            legacy.Root.SetAttributeValue("Units", "mm");
            try { ChamberLayoutDocument.Parse(legacy.ToString()); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "不将毫米布局静默当成逻辑尺寸读取");

            foreach (var item in cases)
            {
                Detach();
                page.Width = double.NaN; page.Height = double.NaN;
                var root = new Grid { Width = item.Width, Height = item.Height, Background = page.Background };
                if (item.Embedded)
                {
                    var canvas = new Grid { Width = 1980, Height = 1080, LayoutTransform = new ScaleTransform(item.Zoom, item.Zoom) };
                    canvas.Children.Add(page);
                    root.Children.Add(new Viewbox { Stretch = Stretch.Fill, Child = canvas });
                }
                else root.Children.Add(page);
                host.RootVisual = root;
                root.Measure(new Size(item.Width, item.Height)); root.Arrange(new Rect(0, 0, item.Width, item.Height));
                root.UpdateLayout(); Program.Pump(120);
                scene.SetViewPreset(ChamberViewPreset.Top);
                Program.Pump(60);
                var center = new Point3D(0, 0.4, 0);
                var device = PresentationSource.FromVisual(scene).CompositionTarget.TransformToDevice;
                Point DevicePoint(Point3D world) => device.Transform(scene.ProjectToRoot(world));
                var origin = DevicePoint(center);
                double xLength = (DevicePoint(center + new Vector3D(1, 0, 0)) - origin).Length;
                double zLength = (DevicePoint(center + new Vector3D(0, 0, 1)) - origin).Length;
                string label = $"{item.Width}x{item.Height} / {(item.Embedded ? "宿主 Fill" : "独立窗口")} / {item.Zoom:0.##}x";
                Check(xLength > 0 && Math.Abs(xLength / zLength - 1) < 1e-6, label + "：三维圆盘横纵比例一致");
                var point = center + new Vector3D(0.2, 0, 0.1);
                Check((scene.UnprojectFromRoot(scene.ProjectToRoot(point), center) - point).Length < 1e-6,
                    label + "：比例补偿后拖动坐标转换仍然正确");
                Check(model.PrepareSave() == before && !model.IsDirty, label + "：缩放不修改逻辑尺寸和保存状态");
                Check(((Grid)page.FindName("DesignCanvas")).RenderSize == new Size(1980, 1080), label + "：沿用统一逻辑画布");
                if (item.Zoom == 1 && (item.Width == 800 || item.Width == 1920 || item.Embedded))
                {
                    model.Selected = model.Document!.BasePart; model.SelectedInspectorTab = 1;
                    Program.Pump(40);
                    Program.Capture(root, Path.Combine(output, $"screen-{item.Width}x{item.Height}-{(item.Embedded ? "fill" : "standalone")}.png"));
                }
            }
        }
        finally
        {
            Detach();
            page.Width = originalWidth; page.Height = originalHeight;
            host.RootVisual = page;
            page.Measure(new Size(originalWidth, originalHeight)); page.Arrange(new Rect(0, 0, originalWidth, originalHeight));
            page.UpdateLayout(); Program.Pump(120);
            scene.SetViewPreset(originalView); model.Selected = originalSelection; model.SelectedInspectorTab = originalTab;
        }
        File.WriteAllLines(Path.Combine(output, "screen-checks.txt"), results);

        void Detach()
        {
            host.RootVisual = null;
            if (page.Parent is Panel panel) panel.Children.Remove(page);
        }
    }
}
