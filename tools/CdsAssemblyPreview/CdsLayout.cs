using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml;
using System.Xml.Linq;
using xyz.Client.Presentation.Controls.ThreeD;

namespace CdsAssemblyPreview;

/// <summary>工具布局。兼容旧版槽/管路 XML，Version=2 增加独立阀门位置；不含控制命令。</summary>
public sealed class CdsLayout
{
    public string ComponentRoot { get; init; } = "";
    public double Width { get; init; }
    public double Height { get; init; }
    public List<CdsNode> Nodes { get; } = [];
    public List<CdsPipe> Pipes { get; } = [];
    public IEnumerable<string> Ports => Nodes.Where(n => n.Kind is not ("Sensor" or "Analog")).SelectMany(n => n.Kind == "Terminal" ? new[] { n.Id + ".Port" }
        : n.Kind is "Valve" or "Hardware" ? new[] { n.Id + ".Inlet", n.Id + ".Outlet" }
        : new[] { n.Id + ".Inlet", n.Id + ".Outlet", n.Id + ".Drain" });

    public Point3D Port(string name)
    {
        int split = name.LastIndexOf('.');
        if (split <= 0) throw new InvalidDataException("Invalid CDS port: " + name);
        var node = Nodes.SingleOrDefault(n => n.Id == name[..split])
            ?? throw new InvalidDataException("Missing CDS node: " + name);
        string port = name[(split + 1)..];
        if (node.Kind == "Terminal" && port == "Port") return new Point3D(node.X, -node.Y, 0);
        if (node.Kind is "Valve" or "Hardware" && port is "Inlet" or "Outlet")
        {
            var normalized = node.VisualKind is FluidHardwareKind.CentrifugalPump or FluidHardwareKind.InlineHeater
                ? ProcessHardwareProjection.Port(node.VisualKind.Value, port == "Outlet") : new Point(port == "Inlet" ? 0 : 1, 0.5);
            var p = node.RotatePoint(new Point(node.X + normalized.X * node.Width, node.Y + normalized.Y * node.Height));
            return new Point3D(p.X, -p.Y, 0);
        }
        if (node.Kind != "Tank") throw new InvalidDataException("Invalid CDS port: " + name);
        var local = port switch
        {
            "Inlet" => new Point3D(-1, 2.13, 0), "Outlet" => new Point3D(1, 1.065, 0),
            "Drain" => new Point3D(0, 0, 0), _ => throw new InvalidDataException("Invalid CDS port: " + name)
        };
        return node.Transform().Transform(local);
    }

    public static CdsLayout Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != "CdsLayout" || (string?)root.Attribute("Version") is not ("1" or "2") || root.Elements("Page").Count() != 1)
            throw new InvalidDataException("Expected CDS layout Version=1 with one Page.");
        var page = root.Element("Page")!;
        var layout = new CdsLayout { ComponentRoot = Text(page, "ComponentRoot"), Width = Number(page, "Width"), Height = Number(page, "Height") };
        if (layout.Width is < 300 or > 4000 || layout.Height is < 300 or > 4000)
            throw new InvalidDataException("Invalid CDS canvas size.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in (page.Element("Elements") ?? throw new InvalidDataException("Missing Elements.")).Elements())
        {
            if (e.Name != "Tank" && e.Name != "Terminal" && e.Name != "Valve" && e.Name != "Sensor" && e.Name != "Analog" && e.Name != "Hardware") throw new InvalidDataException("Unknown CDS node.");
            bool tank = e.Name == "Tank";
            bool device = e.Name != "Terminal";
            if (tank && Text(e, "Template") != "StandardTank") throw new InvalidDataException("Unknown CDS tank template.");
            string id = Text(e, "Id");
            if (string.IsNullOrWhiteSpace(id) || id.Contains('.') || !ids.Add(id)) throw new InvalidDataException("Duplicate or invalid CDS ID.");
            var node = new CdsNode(e.Name.LocalName, id, device ? Text(e, "Component") : "", Text(e, "Text"),
                Number(e, "X"), Number(e, "Y"), Number(e, "Width", device ? null : 12), Number(e, "Height", device ? null : 12),
                Number(e, "Angle", 0), Number(e, "WallTransparency", 0.72), Boolean(e, "SensorAlarm", false),
                e.Name == "Hardware" && Enum.TryParse<FluidHardwareKind>((string?)e.Attribute("VisualKind"), out var visual) && Enum.IsDefined(visual) ? visual : null);
            if (e.Name == "Hardware" && node.VisualKind is null) throw new InvalidDataException("Missing hardware visual kind.");
            layout.CheckPoint(new Point(node.X, node.Y));
            if (node.Width < (tank ? 40 : 8) || node.Height < (tank ? 40 : 8) || node.Width > layout.Width || node.Height > layout.Height
                || device && (node.X + node.Width > layout.Width || node.Y + node.Height > layout.Height)
                || node.Angle is < -360 or > 360 || node.WallTransparency is < 0 or > 1)
                throw new InvalidDataException("Invalid CDS tank size or position.");
            layout.Nodes.Add(node);
        }
        if (layout.Nodes.Count > 100) throw new InvalidDataException("Too many CDS nodes.");
        foreach (var e in (page.Element("Pipes") ?? throw new InvalidDataException("Missing Pipes.")).Elements())
        {
            if (e.Name != "Pipe") throw new InvalidDataException("Unknown CDS pipe.");
            string id = Text(e, "Id"), mode = Text(e, "PreviewMode"), from = Text(e, "From"), to = Text(e, "To");
            if (!ids.Add(id) || string.IsNullOrWhiteSpace(id) || from == to)
                throw new InvalidDataException("Invalid CDS route.");
            var points = e.Element("Bends")?.Elements("Point").Select(p => new Point(Number(p, "X"), Number(p, "Y"))).ToArray() ?? [];
            if (points.Length > 100) throw new InvalidDataException("Too many CDS bends.");
            foreach (var p in points) layout.CheckPoint(p);
            foreach (string port in new[] { from, to }) { var p = layout.Port(port); layout.CheckPoint(new Point(p.X, -p.Y)); }
            var pipe = new CdsPipe(id, from, to, mode, (string?)e.Attribute("Text") ?? id, points,
                Number(e, "Diameter", 16), Number(e, "WallThickness", 1.6), Number(e, "BendRadius", 18), Number(e, "WallOpacity", 0.18),
                Number(e, "FlowSpeed", 65), ColorValue(e, "WallColor", "#B4B4B4"), ColorValue(e, "FlowColor", "#60BCDE"),
                Boolean(e, "ReverseFlow", false), Boolean(e, "IsFilled", true), Boolean(e, "IsAnimationEnabled", true), Number(e, "FluidCoreRatio", 0.58));
            if (pipe.Diameter is < 2 or > 80 || pipe.WallThickness <= 0 || pipe.WallThickness >= pipe.Diameter / 2
                || pipe.BendRadius is < 0 or > 300 || pipe.WallOpacity is < 0 or > 1 || pipe.FlowSpeed is < 0 or > 500 || pipe.FluidCoreRatio <= 0 || pipe.FluidCoreRatio > 1)
                throw new InvalidDataException("Invalid CDS pipe appearance.");
            layout.Pipes.Add(pipe);
        }
        if (layout.Pipes.Count > 200) throw new InvalidDataException("Too many CDS pipes.");
        return layout;
    }

    public string ToXml()
    {
        var nodes = new XElement("Elements", Nodes.Select(n =>
        {
            var e = new XElement(n.Kind, new XAttribute("Id", n.Id), new XAttribute("Text", n.Text), new XAttribute("X", n.X), new XAttribute("Y", n.Y));
            e.Add(new XAttribute("Width", Math.Max(8, n.Width)), new XAttribute("Height", Math.Max(8, n.Height)), new XAttribute("Angle", n.Angle));
            if (n.Kind != "Terminal") e.Add(new XAttribute("Component", n.Component));
            if (n.Kind == "Tank") e.Add(new XAttribute("Template", "StandardTank"));
            if (n.Kind == "Tank") e.Add(new XAttribute("WallTransparency", n.WallTransparency));
            if (n.Kind == "Sensor") e.Add(new XAttribute("SensorAlarm", n.SensorAlarm));
            if (n.Kind == "Hardware") e.Add(new XAttribute("VisualKind", n.VisualKind!));
            return e;
        }));
        var pipes = new XElement("Pipes", Pipes.Select(p => new XElement("Pipe", new XAttribute("Id", p.Id), new XAttribute("From", p.From),
            new XAttribute("To", p.To), new XAttribute("Medium", "IPA"), new XAttribute("Text", p.Text), new XAttribute("PreviewMode", p.Kind),
            new XAttribute("Diameter", p.Diameter), new XAttribute("WallThickness", p.WallThickness), new XAttribute("BendRadius", p.BendRadius),
            new XAttribute("WallOpacity", p.WallOpacity), new XAttribute("FlowSpeed", p.FlowSpeed), new XAttribute("WallColor", p.WallColor),
            new XAttribute("FlowColor", p.FlowColor), new XAttribute("ReverseFlow", p.ReverseFlow), new XAttribute("IsFilled", p.IsFilled),
            new XAttribute("IsAnimationEnabled", p.IsAnimationEnabled), new XAttribute("FluidCoreRatio", p.FluidCoreRatio), new XElement("Bends", p.Bends.Select(b =>
                new XElement("Point", new XAttribute("X", b.X), new XAttribute("Y", b.Y)))))));
        return new XDocument(new XElement("CdsLayout", new XAttribute("Version", "2"), new XElement("Page", new XAttribute("Id", "IPA"),
            new XAttribute("Title", "CDS"), new XAttribute("ComponentRoot", ComponentRoot), new XAttribute("Width", Width), new XAttribute("Height", Height), nodes, pipes))).ToString();
    }

    private void CheckPoint(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X < 0 || point.Y < 0 || point.X > Width || point.Y > Height)
            throw new InvalidDataException("CDS point is outside the canvas.");
    }
    private static string Text(XElement e, string name) => (string?)e.Attribute(name) ?? throw new InvalidDataException("Missing " + name);
    private static double Number(XElement e, string name, double? fallback = null)
    {
        if (e.Attribute(name) is null && fallback.HasValue) return fallback.Value;
        if (!double.TryParse(Text(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new InvalidDataException("Invalid " + name);
        return value;
    }
    private static bool Boolean(XElement e, string name, bool fallback) => e.Attribute(name) is null ? fallback :
        bool.TryParse(Text(e, name), out bool value) ? value : throw new InvalidDataException("Invalid " + name);
    private static Color ColorValue(XElement e, string name, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString((string?)e.Attribute(name) ?? fallback); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        { throw new InvalidDataException("Invalid " + name, ex); }
    }
}

public sealed record CdsNode(string Kind, string Id, string Component, string Text, double X, double Y, double Width, double Height,
    double Angle = 0, double WallTransparency = 0.72, bool SensorAlarm = false, FluidHardwareKind? VisualKind = null)
{
    public string DisplayName => Component.Length > 0 ? Component : string.IsNullOrWhiteSpace(Text) ? Id : Text;
    public Point RotatePoint(Point point) => new RotateTransform(Angle, X + Width / 2, Y + Height / 2).Transform(point);
    public Transform3DGroup Transform()
    {
        var group = new Transform3DGroup();
        group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 12)));
        group.Children.Add(new ScaleTransform3D(Width / (TankVisual3D.RimRadius * 2), Height / TankVisual3D.TankHeight, Width / 2));
        group.Children.Add(new TranslateTransform3D(X + Width / 2, -Y - Height, 0));
        group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -Angle), X + Width / 2, -Y - Height / 2, 0));
        return group;
    }
}

public sealed record CdsPipe(string Id, string From, string To, string Kind, string Text, IReadOnlyList<Point> Bends,
    double Diameter = 16, double WallThickness = 1.6, double BendRadius = 18, double WallOpacity = 0.5, double FlowSpeed = 65,
    Color WallColor = default, Color FlowColor = default, bool ReverseFlow = false, bool IsFilled = true, bool IsAnimationEnabled = true, double FluidCoreRatio = 0.58);
