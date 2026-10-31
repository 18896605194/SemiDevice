using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml;
using System.Xml.Linq;
using xyz.Client.Presentation.Controls.ThreeD;

namespace CdsAssemblyPreview;

/// <summary>普通 UserControl XAML。几何在 XAML 中，实时值直接绑定元件的依赖属性。</summary>
public static class CdsDiagramXaml
{
    private static readonly XNamespace W = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace F = "clr-namespace:xyz.Client.Presentation.Controls.Fluid;assembly=xyz.Client.Presentation";
    private static readonly XNamespace T = "clr-namespace:xyz.Client.Presentation.Controls.ThreeD;assembly=xyz.Client.Presentation";
    private static readonly XNamespace C = "clr-namespace:xyz.Client.Presentation.Controls;assembly=xyz.Client.Presentation";
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static XAttribute A(string name, object value) => new(name, value is double n ? Number(n) : value);
    private static XAttribute Meta(string name, string value) => new(F + "CdsDiagram." + name, value);
    private static string State(string component, string property) => "{Binding DataContext.DiagramStates[" + component + "]." + property + ", ElementName=CdsRoot, Mode=OneWay}";
    private static string PipeState(string id, string property) => "{Binding DataContext.DiagramPipes[" + id + "]." + property + ", ElementName=CdsRoot, Mode=OneWay}";
    private static XElement Label(string text, double y, string size = "Size14") => new(W + "TextBlock", A("Text", text), A("Canvas.Top", y),
        A("Foreground", "{DynamicResource DarkPrimaryText}"), A("FontSize", "{DynamicResource " + size + "}"));

    public static string Export(CdsLayout layout)
    {
        // 使用现有布局校验边界、管口、类型及数值。
        CdsLayout.Parse(layout.ToXml());
        var stage = new XElement(W + "Grid", new XAttribute(X + "Name", "CdsStage"), A("Width", layout.Width), A("Height", layout.Height));
        var root = new XElement(W + "UserControl", new XAttribute(XNamespace.Xmlns + "x", X), new XAttribute(XNamespace.Xmlns + "fluid", F),
            new XAttribute(XNamespace.Xmlns + "threeD", T), new XAttribute(XNamespace.Xmlns + "controls", C), new XAttribute(X + "Name", "CdsRoot"),
            Meta("ComponentRoot", layout.ComponentRoot), A("HorizontalContentAlignment", "Stretch"), A("VerticalContentAlignment", "Stretch"),
            A("UseLayoutRounding", true), A("SnapsToDevicePixels", true),
            new XElement(W + "Grid", A("Background", "{DynamicResource DarkSurfaceBackground}"), A("ClipToBounds", true),
                new XElement(W + "Viewbox", A("Stretch", "Uniform"), A("StretchDirection", "Both"), stage)));
        var viewport = Viewport(layout.Width, layout.Height);
        stage.Add(viewport);
        foreach (var p in layout.Pipes)
        {
            var points = new[] { layout.Port(p.From) }.Concat(p.Bends.Select(b => new Point3D(b.X, -b.Y, 0))).Append(layout.Port(p.To));
            var pipe = new XElement(T + "FluidRouteVisual3D", Meta("Id", p.Id), Meta("From", p.From), Meta("To", p.To), Meta("RouteKind", p.Kind), Meta("Text", p.Text),
                A("Path", string.Join(" ", points.Select(v => Number(v.X * .01) + "," + Number(v.Y * .01) + "," + Number(v.Z * .01)))),
                A("PipeRadius", p.Diameter * .005), A("WallThickness", p.WallThickness * .01), A("BendRadius", p.BendRadius * .01),
                A("WallOpacity", p.WallOpacity), A("WallColor", p.WallColor), A("FlowColor", p.FlowColor), A("FlowSpeed", p.FlowSpeed * .01),
                A("ReverseFlow", p.ReverseFlow), A("FluidCoreRatio", p.FluidCoreRatio), A("IsAnimationEnabled", p.IsAnimationEnabled),
                A("IsFlowing", PipeState(p.Id, "IsFlowing")), A("IsFilled", p.IsFilled ? PipeState(p.Id, "IsFilled") : "False"),
                new XElement(T + "FluidRouteVisual3D.Transform", new XElement(W + "ScaleTransform3D", A("ScaleX", 100d), A("ScaleY", 100d), A("ScaleZ", 100d))));
            viewport.Add(pipe);
        }
        var canvas = new XElement(W + "Canvas"); stage.Add(canvas);
        foreach (var n in layout.Nodes)
        {
            var node = new XElement(W + "Grid", Meta("Id", n.Id), Meta("Kind", n.Kind), Meta("Component", n.Component),
                A("Canvas.Left", n.Kind == "Terminal" ? n.X - n.Width / 2 : n.X), A("Canvas.Top", n.Kind == "Terminal" ? n.Y - n.Height / 2 : n.Y),
                A("Width", n.Width), A("Height", n.Height), A("RenderTransformOrigin", "0.5,0.5"),
                new XElement(W + "Grid.RenderTransform", new XElement(W + "RotateTransform", A("Angle", n.Angle))));
            canvas.Add(node);
            XElement visual;
            if (n.Kind == "Tank")
            {
                // 接头、罐口和底部超出罐身矩形；通过负 Margin 扩大正交视口，位置仍由父 Grid 声明。
                var tankView = Viewport(n.Width, n.Height, n.Width / 2);
                var tank = new XElement(T + "TankVisual3D", A("WallTransparency", n.WallTransparency),
                    A("FillFraction", State(n.Component, "FillFraction")), A("IsAlarm", State(n.Component, "IsAlarm")),
                    new XElement(T + "TankVisual3D.Transform", new XElement(W + "Transform3DGroup",
                        new XElement(W + "RotateTransform3D", new XElement(W + "RotateTransform3D.Rotation", new XElement(W + "AxisAngleRotation3D", A("Axis", "1,0,0"), A("Angle", 12d)))),
                        new XElement(W + "ScaleTransform3D", A("ScaleX", n.Width / (TankVisual3D.RimRadius * 2)), A("ScaleY", n.Height / TankVisual3D.TankHeight), A("ScaleZ", n.Width / 2)),
                        new XElement(W + "TranslateTransform3D", A("OffsetX", n.Width / 2), A("OffsetY", -n.Height)))));
                tankView.Add(tank); visual = tankView;
            }
            else if (n.Kind == "Valve") visual = new XElement(F + "ValveSymbol", A("IsOpen", State(n.Component, "IsOpen")));
            else if (n.Kind == "Sensor") visual = new XElement(W + "Viewbox", new XElement(C + "SignalLamp", A("Text", n.Text), A("IsActive", State(n.Component, "IsActive")), A("IsAlarm", n.SensorAlarm)));
            else if (n.Kind == "Hardware")
            {
                bool process = n.VisualKind is FluidHardwareKind.CentrifugalPump or FluidHardwareKind.InlineHeater;
                visual = new XElement(F + (process ? "ProcessHardwareScene" : "HardwareVisual"), A("Kind", n.VisualKind!),
                    A("State", State(n.Component, "State")), A("Reading", State(n.Component, "Reading")), A("Target", State(n.Component, "Target")));
                if (process) { visual.Add(A("ViewMode", "Front2D"), A("PowerFraction", State(n.Component, "PowerFraction"))); }
                else visual.Add(A("Unit", State(n.Component, "Unit")));
            }
            else if (n.Kind == "Analog") visual = new XElement(W + "TextBlock", A("Text", State(n.Component, "ReadingText")),
                A("VerticalAlignment", "Center"), A("TextAlignment", "Center"), A("Foreground", "{DynamicResource DarkPrimaryText}"), A("FontSize", "{DynamicResource Size14}"));
            else visual = new XElement(W + "Rectangle", A("Stroke", "{DynamicResource DarkDisabledText}"), A("StrokeThickness", 2), A("Fill", "{DynamicResource DarkSurfaceBackground}"));
            node.Add(visual);
            // 标签与模拟数值分开声明。管路不生成补液、排液等动作文字。
            if (n.Kind != "Sensor")
            {
                var labels = new XElement(W + "Canvas", A("IsHitTestVisible", false), Label(n.Text, n.Kind == "Tank" ? -74 : n.Kind == "Hardware" ? -28 : n.Height + 6, n.Kind == "Tank" ? "Size22" : "Size14"));
                labels.AddFirst(new XElement(W + "Canvas.RenderTransform", new XElement(W + "RotateTransform", A("Angle", -n.Angle), A("CenterX", n.Width / 2), A("CenterY", n.Height / 2))));
                if (n.Kind is "Tank" or "Hardware") { var reading = Label("", n.Height + 18); reading.SetAttributeValue("Text", State(n.Component, "ReadingText")); labels.Add(reading); }
                node.Add(labels);
            }
        }
        return root.ToString();
    }

    private static XElement Viewport(double width, double height, double padding = 0) => new(W + "Viewport3D", A("IsHitTestVisible", false), A("Margin", Number(-padding)),
        new XElement(W + "Viewport3D.Camera", new XElement(W + "OrthographicCamera", A("Position", Number(width / 2) + "," + Number(-height / 2) + ",2000"),
            A("LookDirection", "0,0,-1"), A("UpDirection", "0,1,0"), A("Width", width + padding * 2))),
        new XElement(W + "ModelVisual3D", new XElement(W + "ModelVisual3D.Content", new XElement(W + "Model3DGroup",
            new XElement(W + "AmbientLight", A("Color", "#FF8C8C8C")), new XElement(W + "DirectionalLight", A("Color", "White"), A("Direction", "-1,-3,-2"))))));

    /// <summary>只读 XML，不执行导入文件中的对象、扩展或事件；显示尺寸与管线取自实际 XAML 属性。</summary>
    public static CdsLayout ReadLayout(string xaml)
    {
        using var reader = XmlReader.Create(new StringReader(xaml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 });
        var root = XElement.Load(reader);
        if (root.Name != W + "UserControl") throw new InvalidDataException("CDS XAML 必须是 UserControl。");
        string Get(XElement e, string name) => (string?)e.Attribute(name) ?? throw new InvalidDataException("缺少属性：" + name);
        string M(XElement e, string name) => (string?)e.Attribute(F + "CdsDiagram." + name) ?? "";
        double N(XElement e, string name) => double.Parse(Get(e, name), CultureInfo.InvariantCulture);
        bool B(XElement e, string name) => bool.Parse(Get(e, name));
        Color ColorOf(XElement e, string name) => (Color)ColorConverter.ConvertFromString(Get(e, name));
        var stage = root.Descendants(W + "Grid").Single(e => (string?)e.Attribute(X + "Name") == "CdsStage");
        var layout = new CdsLayout { ComponentRoot = M(root, "ComponentRoot"), Width = N(stage, "Width"), Height = N(stage, "Height") };
        foreach (var e in stage.Elements(W + "Canvas").Elements(W + "Grid"))
        {
            string kind = M(e, "Kind");
            double width = N(e, "Width"), height = N(e, "Height");
            double x = N(e, "Canvas.Left"), y = N(e, "Canvas.Top");
            if (kind == "Terminal") { x += width / 2; y += height / 2; }
            var label = e.Elements(W + "Canvas").Elements(W + "TextBlock").FirstOrDefault();
            var lamp = e.Descendants(C + "SignalLamp").SingleOrDefault();
            var tank = e.Descendants(T + "TankVisual3D").SingleOrDefault();
            var hardware = e.Elements().SingleOrDefault(v => v.Name == F + "ProcessHardwareScene" || v.Name == F + "HardwareVisual");
            layout.Nodes.Add(new CdsNode(kind, M(e, "Id"), M(e, "Component"), (string?)lamp?.Attribute("Text") ?? (string?)label?.Attribute("Text") ?? "",
                x, y, width, height, N(e.Element(W + "Grid.RenderTransform")!.Element(W + "RotateTransform")!, "Angle"),
                tank is null ? .72 : N(tank, "WallTransparency"), lamp is not null && B(lamp, "IsAlarm"),
                hardware is null ? null : Enum.Parse<FluidHardwareKind>(Get(hardware, "Kind"))));
        }
        foreach (var e in stage.Elements(W + "Viewport3D").Elements(T + "FluidRouteVisual3D"))
        {
            var path = Point3DCollection.Parse(Get(e, "Path"));
            if (path.Count < 2 || path.Count > 202) throw new InvalidDataException("管路路径无效。");
            layout.Pipes.Add(new CdsPipe(M(e, "Id"), M(e, "From"), M(e, "To"), M(e, "RouteKind"), M(e, "Text"),
                path.Skip(1).SkipLast(1).Select(p => new Point(p.X * 100, -p.Y * 100)).ToArray(), N(e, "PipeRadius") * 200,
                N(e, "WallThickness") * 100, N(e, "BendRadius") * 100, N(e, "WallOpacity"), N(e, "FlowSpeed") * 100,
                ColorOf(e, "WallColor"), ColorOf(e, "FlowColor"), B(e, "ReverseFlow"), !bool.TryParse(Get(e, "IsFilled"), out bool filled) || filled, B(e, "IsAnimationEnabled"), N(e, "FluidCoreRatio")));
        }
        return CdsLayout.Parse(layout.ToXml());
    }
}
