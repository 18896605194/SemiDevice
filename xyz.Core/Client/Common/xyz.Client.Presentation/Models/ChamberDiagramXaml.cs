using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using xyz.Client.Presentation.Controls.ThreeD;

namespace xyz.Client.Presentation.Models;

/// <summary>导出实际控件、WPF 变换及依赖属性 Binding；安装计算只在导出时进行。</summary>
public static class ChamberDiagramXaml
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace ThreeD = "clr-namespace:xyz.Client.Presentation.Controls.ThreeD;assembly=xyz.Client.Presentation";
    private static readonly Dictionary<string, string> Types = new()
    {
        ["Base"] = "ChamberBaseVisual3D", ["Disk"] = "DiskVisual3D", ["Bowl"] = "BowlVisual3D", ["Door"] = "DoorVisual3D",
        ["Arm"] = "ArmVisual3D", ["Lift"] = "LiftVisual3D", ["Nozzle"] = "FluidPipeVisual3D", ["HomeCup"] = "HomeCupVisual3D"
    };
    private static string StateBinding(string id, string property) => "{Binding DataContext.DiagramStates[" + id + "]." + property + ", ElementName=ChamberRoot, Mode=OneWay}";
    private static string MountBinding(string name) => "{Binding MountHeight, ElementName=" + name + ", Mode=OneWay}";
    private static XElement Translate(object x, object y, object z) => new(Wpf + "TranslateTransform3D", new XAttribute("OffsetX", x), new XAttribute("OffsetY", y), new XAttribute("OffsetZ", z));
    private static XElement Scale(double x, double y, double z) => new(Wpf + "ScaleTransform3D", new XAttribute("ScaleX", x), new XAttribute("ScaleY", y), new XAttribute("ScaleZ", z));
    private static XElement Rotate(double angle) => new(Wpf + "RotateTransform3D", new XElement(Wpf + "RotateTransform3D.Rotation",
        new XElement(Wpf + "AxisAngleRotation3D", new XAttribute("Axis", "0,1,0"), new XAttribute("Angle", angle))));

    public static string Export(ChamberLayoutModel layout, ChamberVisualCatalog? catalog = null)
    {
        var keys = layout.Parts.Select((p, i) => (p.Id, Key: "Visual" + i)).ToDictionary(p => p.Id, p => p.Key);
        double ArmLength(ChamberPartLayout arm)
        {
            var disk = layout.Parts.FirstOrDefault(p => p.Kind == "Disk");
            var lift = layout.Parts.FirstOrDefault(p => p.ParentId == arm.Id && p.Kind == "Lift");
            double yaw = arm.Yaw * Math.PI / 180, x = lift?.X ?? 0, z = lift?.Z ?? 0;
            double dx = (disk?.X ?? arm.X + arm.Size("ArmLength")) - arm.X - x * Math.Cos(yaw) - z * Math.Sin(yaw);
            double dz = (disk?.Z ?? arm.Z) - arm.Z + x * Math.Sin(yaw) - z * Math.Cos(yaw);
            return arm.AutoLength ? Math.Max(0.05, Math.Sqrt(dx * dx + dz * dz)) : arm.Size("ArmLength");
        }
        XElement Mount(ChamberPartLayout arm, double height)
        {
            var lift = layout.Parts.FirstOrDefault(p => p.ParentId == arm.Id && p.Kind == "Lift");
            var group = new XElement(Wpf + "Transform3DGroup");
            if (lift is null) group.Add(Translate(0, 0.85, 0));
            else
            {
                // 变换顺序抵消几何缩放，只把安装高度随气缸高度比例变化；联动由 WPF Binding 完成。
                double sy = lift.Size("HeightScale");
                group.Add(Scale(1, 1 / sy, 1));
                group.Add(Translate(0, MountBinding(keys[lift.Id]), 0));
                group.Add(Scale(1, sy, 1));
                group.Add(Translate(lift.X / 0.7, lift.Y / 0.7, lift.Z / 0.7));
            }
            group.Add(Translate(0, height, 0)); return group;
        }
        XElement Part(ChamberPartLayout part)
        {
            var element = new XElement(ThreeD + "ChamberDiagramPart3D", Metadata(part), new XAttribute("Kind", part.Kind),
                new XElement(ThreeD + "ChamberDiagramPart3D.Sizes",
                    part.Sizes.Select(s => new XElement(ThreeD + "ChamberDiagramSize", new XAttribute("Key", s.Key), new XAttribute("Value", s.Value)))));
            double parentScale = part.ParentId.Length > 0 ? 0.7 : 1;
            double sx = part.Kind == "Arm" ? 0.7 : part.Kind == "Lift" ? part.Size("WidthScale") : 1;
            double sy = part.Kind is "Bowl" or "Lift" ? part.Size("HeightScale") : part.Kind == "Nozzle" ? part.Size("DiameterScale") : sx;
            double sz = part.Kind == "Door" ? part.Size("DepthScale") : part.Kind == "Nozzle" ? part.Size("DiameterScale") : sx;
            var transform = new XElement(Wpf + "Transform3DGroup", Scale(sx, sy, sz), Rotate(part.Yaw));
            if (part.Kind == "HomeCup" && part.ParentId.Length > 0)
            {
                var arm = layout.Parts.Single(p => p.Id == part.ParentId); double home = arm.HomeAngle * Math.PI / 180;
                transform.Add(Mount(arm, 0));
                transform.Add(Translate(ArmLength(arm) / 0.7 * Math.Cos(home), 0, -ArmLength(arm) / 0.7 * Math.Sin(home)));
            }
            transform.Add(Translate(part.X / parentScale, part.Y / parentScale, part.Z / parentScale));
            element.Add(new XElement(ThreeD + "ChamberDiagramPart3D.Transform", transform));
            element.Add(new XElement(ThreeD + "ChamberDiagramPart3D.Visual", Visual(part)));
            element.Add(layout.Parts.Where(p => p.ParentId == part.Id && p.Kind != "Nozzle").Select(Part));
            return element;
        }
        XElement Visual(ChamberPartLayout part)
        {
            var visual = new XElement(ThreeD + Types[part.Kind], new XAttribute(X + "Name", keys[part.Id]));
            void Set(string property, object value) => visual.SetAttributeValue(property, value);
            void Bind(string property, string? stateProperty = null) => Set(property, StateBinding(part.Component, stateProperty ?? property));
            foreach (string key in new[] { "Radius", "Width", "Height", "Thickness", "SpindleHeight" })
                if (part.Sizes.TryGetValue(key, out double value)) Set(key, value);
            if (part.Kind is "Arm" or "Disk") { Bind("IsMoving"); Bind("IsError"); }
            if (part.Kind is "Bowl" or "Lift" or "Door")
            { Bind(part.Kind == "Door" ? "IsOpen" : "IsRaised", "IsRaised"); Bind("IsUnknown"); }
            if (part.Kind == "Base") Set("Length", part.Size("Length"));
            if (part.Kind == "Disk")
            { Set("IsDiskVisible", true); Bind("RotationSpeed"); Bind("RotateClockwise"); Set("Data", "{Binding DataContext.Model.Wafer, ElementName=ChamberRoot, Mode=OneWay}"); }
            if (part.Kind == "Arm")
            {
                Set("Length", ArmLength(part) / 0.7); Set("Width", part.Size("ArmWidth") / 0.7);
                Set("Thickness", part.Size("ArmThickness") / 0.7); Set("PivotRadius", part.Size("PivotRadius") / 0.7); Bind("Angle");
                var transform = Mount(part, part.Size("ArmThickness") / 0.7 * 1.5);
                transform.AddFirst(Rotate(part.HomeAngle));
                visual.Add(new XElement(ThreeD + "ArmVisual3D.Transform", transform));
                var moving = layout.Parts.Where(p => p.ParentId == part.Id && p.Kind == "Nozzle").ToArray();
                if (moving.Length > 0) visual.Add(new XElement(ThreeD + "ArmVisual3D.Attachments", moving.Select(Part)));
            }
            if (part.Kind == "Nozzle")
            {
                double length = part.AutoLength && part.ParentId.Length > 0 ? ArmLength(layout.Parts.Single(p => p.Id == part.ParentId)) : part.Size("Length");
                double scale = part.ParentId.Length > 0 ? 0.7 : 1;
                Set("Length", length * part.Size("LengthScale") / scale); Set("StreamLength", part.Size("StreamLength") / scale); Bind("IsFlowing");
            }
            if (part.Kind == "HomeCup" && part.ParentId.Length > 0)
            { Set("Radius", part.Size("Radius") / 0.7); Set("Height", part.Size("Height") / 0.7); }
            using var declared = catalog?.CreateProjectVisual(new ChamberVisualContext(part.Kind, layout.ComponentRoot, part.Component, part.ParentId, layout.ChamberType));
            if (declared is not null)
            {
                // 项目控件在导出时选定，正式页面直接创建 XAML 声明的类型，不再用运行时适配器替换。
                var sizes = new Dictionary<string, double>(part.Sizes);
                foreach (var a in visual.Attributes().Where(a => a.Name.Namespace == XNamespace.None))
                    if (double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) sizes[a.Name.LocalName] = value;
                declared.Apply(sizes, new ChamberVisualFeedback(Animate: true));
                Type type = declared.Visual.GetType();
                XNamespace ns = "clr-namespace:" + type.Namespace + ";assembly=" + type.Assembly.GetName().Name;
                visual.Name = ns + type.Name;
                foreach (var child in visual.Elements().Where(e => e.Name.Namespace == ThreeD).ToArray())
                    child.Name = ns + type.Name + "." + child.Name.LocalName.Split('.')[^1];
                foreach (string property in new[] { "BodyColor", "HighlightColor", "HighlightStrength", "FlowColor", "WallOpacity", "HeightLevel", "IsAnimationEnabled" })
                {
                    var descriptor = DependencyPropertyDescriptor.FromName(property, type, type);
                    if (descriptor is not null && declared.Visual.ReadLocalValue(descriptor.DependencyProperty) != DependencyProperty.UnsetValue)
                        Set(property, TypeDescriptor.GetConverter(descriptor.PropertyType).ConvertToInvariantString(descriptor.GetValue(declared.Visual))!);
                }
            }
            return visual;
        }
        var world = new XElement(ThreeD + "ChamberDiagramRoot3D", new XAttribute(X + "Name", "ChamberWorld"),
            new XAttribute("ComponentRoot", layout.ComponentRoot), new XAttribute("ChamberType", layout.ChamberType));
        world.Add(new XElement(Wpf + "ModelVisual3D", new XElement(Wpf + "ModelVisual3D.Content", new XElement(Wpf + "Model3DGroup",
            new XElement(Wpf + "AmbientLight", new XAttribute("Color", "#808080")),
            new XElement(Wpf + "DirectionalLight", new XAttribute("Color", "White"), new XAttribute("Direction", "-1,-3,-2"))))));
        world.Add(layout.Parts.Where(p => p.ParentId.Length == 0).Select(Part));
        var root = CreateView(world);
        int prefix = 0;
        foreach (var ns in root.Descendants().Select(e => e.Name.Namespace).Distinct().Where(ns => ns != Wpf && ns != ThreeD))
            root.Add(new XAttribute(XNamespace.Xmlns + ("project" + prefix++), ns));
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true, NewLineOnAttributes = true })) root.Save(writer);
        string result = text.ToString(); ReadLayout(result); return result;
    }

    public static ChamberLayoutModel ReadLayout(string xaml)
    {
        using var reader = XmlReader.Create(new StringReader(xaml), ReaderSettings());
        return ReadLayout(Normalize(XDocument.Load(reader).Root!));
    }
    private static ChamberLayoutModel ReadLayout(XElement root)
    {
        var world = root.Descendants(ThreeD + "ChamberDiagramRoot3D").SingleOrDefault();
        if (root.Name != Wpf + "UserControl" || (string?)root.Attribute(X + "Name") != "ChamberRoot" || world is null
            || world.Parent?.Name != Wpf + "Viewport3D" || root.Descendants(Wpf + "Viewport3D").Count() != 1)
            throw new InvalidDataException("Expected a chamber UserControl XAML view.");
        var parts = root.Descendants(ThreeD + "ChamberDiagramPart3D").ToArray();
        var natives = parts.Select(p => p.Element(ThreeD + "ChamberDiagramPart3D.Visual")?.Elements().SingleOrDefault()
            ?? throw new InvalidDataException("Missing inline chamber control.")).ToDictionary(e => (string?)e.Attribute(X + "Name") ?? "");
        var names = root.DescendantsAndSelf().Attributes(X + "Name").Select(a => a.Value).ToArray();
        if (names.Distinct().Count() != names.Length || names.Any(n => !Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_]*$")))
            throw new InvalidDataException("Invalid chamber control names.");
        var expressions = new HashSet<string> { "{Binding DataContext.Model.Wafer, ElementName=ChamberRoot, Mode=OneWay}", "{DynamicResource DarkControlBackground}" };
        foreach (var part in parts)
        {
            string id = (string?)part.Attribute("Component") ?? "";
            if (id.IndexOfAny(['[', ']', ',', '{', '}', '^']) >= 0) throw new InvalidDataException("Invalid component path.");
            foreach (string property in new[] { "Angle", "IsMoving", "IsError", "IsRaised", "IsUnknown", "IsFlowing", "RotationSpeed", "RotateClockwise" }) expressions.Add(StateBinding(id, property));
        }
        foreach (string key in natives.Keys)
            expressions.Add(MountBinding(key));
        var wpfNames = new HashSet<string> { "UserControl", "Grid", "Viewbox", "Viewport3D", "Viewport3D.Camera", "OrthographicCamera", "ModelVisual3D", "ModelVisual3D.Content", "Model3DGroup", "AmbientLight", "DirectionalLight",
            "Transform3DGroup", "ScaleTransform3D", "TranslateTransform3D", "RotateTransform3D", "RotateTransform3D.Rotation", "AxisAngleRotation3D" };
        var controlNames = Types.Values.Concat(Types.Values.Select(t => t + ".Transform")).Concat(new[] { "ChamberDiagramRoot3D", "ChamberDiagramPart3D",
            "ChamberDiagramPart3D.Transform", "ChamberDiagramPart3D.Visual", "ChamberDiagramPart3D.Sizes", "ChamberDiagramSize", "ArmVisual3D.Attachments" }).ToHashSet();
        var projectNames = natives.Values.Select(e => e.Name).SelectMany(n => new[] { n, n.Namespace + n.LocalName + ".Transform", n.Namespace + n.LocalName + ".Attachments" }).ToHashSet();
        var nativeTypes = natives.Values.Select(e => e.Name).Distinct().ToDictionary(n => n, ResolveVisualType);
        var attributes = new HashSet<string> { "ComponentRoot", "ChamberType", "Id", "Kind", "Component", "ParentId", "X", "Y", "Z", "Yaw", "HomeAngle", "AutoLength", "Visual", "Key", "Value",
            "Color", "Direction", "Radius", "Width", "Height", "Thickness", "SpindleHeight", "Length", "PivotRadius", "Angle", "IsDiskVisible", "IsRaised", "IsOpen", "IsUnknown", "IsMoving", "IsError",
            "IsFlowing", "IsAnimationEnabled", "StreamLength", "RotationSpeed", "RotateClockwise", "Data", "OffsetX", "OffsetY", "OffsetZ", "ScaleX", "ScaleY", "ScaleZ", "Axis", "BodyColor", "HighlightColor", "HighlightStrength", "FlowColor", "WallOpacity", "HeightLevel", "IsDraining",
            "HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment", "UseLayoutRounding", "SnapsToDevicePixels", "ClipToBounds", "Background", "Stretch", "StretchDirection", "Position", "LookDirection", "UpDirection" };
        foreach (var node in root.DescendantsAndSelf())
        {
            if (!(node.Name.Namespace == Wpf && wpfNames.Contains(node.Name.LocalName) || node.Name.Namespace == ThreeD && controlNames.Contains(node.Name.LocalName) || projectNames.Contains(node.Name))
                || node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) throw new InvalidDataException("Unsupported chamber XAML element.");
            foreach (var a in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                bool nativeProperty = nativeTypes.TryGetValue(node.Name, out var type)
                    && DependencyPropertyDescriptor.FromName(a.Name.LocalName, type, type) is not null;
                bool stateBinding = nativeProperty && Regex.IsMatch(a.Value,
                    @"^\{Binding DataContext\.(DiagramStates\[[^\[\]{}]+\]\.[A-Za-z_][A-Za-z0-9_.]*|Model.Wafer), ElementName=ChamberRoot, Mode=OneWay\}$");
                if (!(a.Name == X + "Name" || a.Name.Namespace == XNamespace.None && (attributes.Contains(a.Name.LocalName) || nativeProperty))
                    || a.Value.Contains('{') && !expressions.Contains(a.Value) && !stateBinding) throw new InvalidDataException("Unsupported chamber XAML property or binding.");
            }
        }
        var layout = new XElement("ChamberLayout", new XAttribute("Version", "3"), new XAttribute("Units", "Logical"),
            new XAttribute("ComponentRoot", (string?)world.Attribute("ComponentRoot") ?? ""), new XAttribute("ChamberType", (string?)world.Attribute("ChamberType") ?? ""));
        foreach (var part in parts)
        {
            string kind = (string?)part.Attribute("Kind") ?? "";
            var visual = part.Element(ThreeD + "ChamberDiagramPart3D.Visual")!.Elements().Single();
            if (!Types.ContainsKey(kind) || ((string?)part.Attribute("ParentId") ?? "") != ((string?)part.Ancestors(ThreeD + "ChamberDiagramPart3D").FirstOrDefault()?.Attribute("Id") ?? ""))
                throw new InvalidDataException("Invalid chamber visual type.");
            Type visualType = ResolveVisualType(visual.Name);
            foreach (var a in visual.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.Namespace == XNamespace.None))
                if (DependencyPropertyDescriptor.FromName(a.Name.LocalName, visualType, visualType) is null)
                    throw new InvalidDataException($"The chamber visual must expose {visualType.Name}.{a.Name.LocalName} as a dependency property.");
            var data = new XElement(kind, part.Attributes().Where(a => a.Name.Namespace == XNamespace.None && a.Name.LocalName is not ("Kind" or "Visual")));
            var placement = part.Element(ThreeD + "ChamberDiagramPart3D.Transform")?.Element(Wpf + "Transform3DGroup")
                ?? throw new InvalidDataException("Missing chamber installation transform.");
            var offset = placement.Elements(Wpf + "TranslateTransform3D").Last();
            double parentScale = (string?)part.Attribute("ParentId") is { Length: > 0 } ? 0.7 : 1;
            double Number(XElement element, string name) => double.Parse((string?)element.Attribute(name) ?? "0", CultureInfo.InvariantCulture);
            data.SetAttributeValue("X", Number(offset, "OffsetX") * parentScale);
            data.SetAttributeValue("Y", Number(offset, "OffsetY") * parentScale);
            data.SetAttributeValue("Z", Number(offset, "OffsetZ") * parentScale);
            data.SetAttributeValue("Yaw", Number(placement.Element(Wpf + "RotateTransform3D")!.Element(Wpf + "RotateTransform3D.Rotation")!.Element(Wpf + "AxisAngleRotation3D")!, "Angle"));
            foreach (var size in part.Element(ThreeD + "ChamberDiagramPart3D.Sizes")?.Elements() ?? [])
                data.Add(new XElement("Size", new XAttribute("Key", (string?)size.Attribute("Key") ?? ""), new XAttribute("Value", (string?)size.Attribute("Value") ?? "")));
            void Size(string property, string dimension, double scale = 1)
            {
                if (visual.Attribute(property) is { } a && data.Elements("Size").FirstOrDefault(s => (string?)s.Attribute("Key") == dimension) is { } size)
                {
                    if (!double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) throw new InvalidDataException("Invalid chamber dimension.");
                    size.SetAttributeValue("Value", value * scale);
                }
            }
            var scaling = placement.Element(Wpf + "ScaleTransform3D")!;
            void ScaleSize(string dimension, string property)
            {
                data.Elements("Size").FirstOrDefault(s => (string?)s.Attribute("Key") == dimension)?.SetAttributeValue("Value", Number(scaling, property));
            }
            if (kind is "Bowl" or "Lift") ScaleSize("HeightScale", "ScaleY");
            if (kind == "Lift") ScaleSize("WidthScale", "ScaleX");
            if (kind == "Door") ScaleSize("DepthScale", "ScaleZ");
            if (kind == "Nozzle") ScaleSize("DiameterScale", "ScaleY");
            foreach (string name in new[] { "Radius", "Width", "Height", "Thickness", "SpindleHeight", "Length" }) Size(name, name, kind == "HomeCup" && (string?)part.Attribute("ParentId") is { Length: > 0 } ? 0.7 : 1);
            if (kind == "Arm") { Size("Length", "ArmLength", 0.7); Size("Width", "ArmWidth", 0.7); Size("Thickness", "ArmThickness", 0.7); Size("PivotRadius", "PivotRadius", 0.7); }
            if (kind == "Nozzle")
            {
                double scale = double.Parse((string)data.Elements("Size").Single(s => (string?)s.Attribute("Key") == "LengthScale").Attribute("Value")!, CultureInfo.InvariantCulture);
                Size("Length", "Length", parentScale / scale); Size("StreamLength", "StreamLength", parentScale);
            }
            layout.Add(data);
        }
        return ChamberLayoutModel.Parse(layout.ToString());
    }

    public static ChamberDiagramContent Load(string xaml)
    {
        using var reader = XmlReader.Create(new StringReader(xaml), ReaderSettings());
        var document = Normalize(XDocument.Load(reader).Root!);
        var layout = ReadLayout(document);
        using var viewReader = document.CreateReader();
        var view = (UserControl)XamlReader.Load(viewReader);
        var viewport = (Viewport3D)view.FindName("ChamberViewport");
        var camera = (OrthographicCamera)viewport.Camera;
        var world = viewport.Children.OfType<ChamberDiagramRoot3D>().Single();
        IEnumerable<ChamberDiagramPart3D> Find(DependencyObject node)
        {
            if (node is ChamberDiagramPart3D part) yield return part;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in Find(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        return new(layout, view, viewport, camera, world, Find(world).ToDictionary(p => p.Id));
    }
    private static XElement CreateView(XElement world) => new(Wpf + "UserControl", new XAttribute("xmlns", Wpf),
        new XAttribute(XNamespace.Xmlns + "x", X), new XAttribute(XNamespace.Xmlns + "threeD", ThreeD),
        new XAttribute(X + "Name", "ChamberRoot"), new XAttribute("HorizontalContentAlignment", "Stretch"), new XAttribute("VerticalContentAlignment", "Stretch"),
        new XAttribute("UseLayoutRounding", true), new XAttribute("SnapsToDevicePixels", true),
        new XElement(Wpf + "Grid", new XAttribute("Background", "{DynamicResource DarkControlBackground}"), new XAttribute("ClipToBounds", true),
            new XElement(Wpf + "Viewbox", new XAttribute("Stretch", "Uniform"), new XAttribute("StretchDirection", "Both"),
                new XElement(Wpf + "Viewport3D", new XAttribute(X + "Name", "ChamberViewport"), new XAttribute("Width", 1280), new XAttribute("Height", 800), new XAttribute("ClipToBounds", true),
                    new XElement(Wpf + "Viewport3D.Camera", new XElement(Wpf + "OrthographicCamera", new XAttribute("Width", 7.8), new XAttribute("Position", "-3.074,7.01,9.46"),
                        new XAttribute("LookDirection", "3.074,-6.71,-9.46"), new XAttribute("UpDirection", "0,1,0"))), world))));

    /// <summary>旧版资源字典仅在读取时转换为普通界面，新的保存和导出始终使用 UserControl。</summary>
    private static XElement Normalize(XElement root)
    {
        if (root.Name != Wpf + "ResourceDictionary") return root;
        var resources = root.Elements().ToDictionary(e => (string?)e.Attribute(X + "Key") ?? "");
        var world = new XElement(root.Element(ThreeD + "ChamberDiagramRoot3D") ?? throw new InvalidDataException("Missing chamber scene."));
        world.Attribute(X + "Key")?.Remove(); world.SetAttributeValue(X + "Name", "ChamberWorld");
        var used = new HashSet<string>();
        while (world.Descendants(ThreeD + "ChamberDiagramPart3D").FirstOrDefault(p => p.Attribute("Visual") is not null) is { } part)
        {
            string reference = (string)part.Attribute("Visual")!;
            if (!reference.StartsWith("{StaticResource ") || !reference.EndsWith('}')) throw new InvalidDataException("Invalid legacy chamber control.");
            string key = reference[16..^1];
            if (!used.Add(key) || used.Count > 512 || !resources.TryGetValue(key, out var declared)) throw new InvalidDataException("Invalid legacy chamber assembly.");
            var visual = new XElement(declared); visual.Attribute(X + "Key")?.Remove(); visual.SetAttributeValue(X + "Name", key);
            part.Attribute("Visual")!.Remove(); part.Add(new XElement(ThreeD + "ChamberDiagramPart3D.Visual", visual));
        }
        foreach (var a in world.DescendantsAndSelf().Attributes().Where(a => a.Value.StartsWith("{Binding", StringComparison.Ordinal)))
        {
            a.Value = a.Value.Replace("Data[", "DataContext.DiagramStates[", StringComparison.Ordinal)
                .Replace("{Binding Wafer,", "{Binding DataContext.Model.Wafer,", StringComparison.Ordinal)
                .Replace("Source={StaticResource ChamberState}", "ElementName=ChamberRoot", StringComparison.Ordinal);
            a.Value = Regex.Replace(a.Value, @"Source=\{StaticResource ([A-Za-z_][A-Za-z0-9_]*)\}", "ElementName=$1");
        }
        var view = CreateView(world);
        foreach (var ns in root.Attributes().Where(a => a.IsNamespaceDeclaration && a.Name.LocalName is not ("xmlns" or "x" or "threeD")))
            view.SetAttributeValue(ns.Name, ns.Value);
        return view;
    }
    private static IEnumerable<XAttribute> Metadata(ChamberPartLayout p) => new[] {
        new XAttribute("Id", p.Id), new XAttribute("Component", p.Component), new XAttribute("ParentId", p.ParentId), new XAttribute("X", p.X), new XAttribute("Y", p.Y),
        new XAttribute("Z", p.Z), new XAttribute("Yaw", p.Yaw), new XAttribute("HomeAngle", p.HomeAngle), new XAttribute("AutoLength", p.AutoLength) };
    private static Type ResolveVisualType(XName name)
    {
        string ns = name.NamespaceName;
        int separator = ns.IndexOf(";assembly=", StringComparison.Ordinal);
        if (!ns.StartsWith("clr-namespace:", StringComparison.Ordinal) || separator < 14) throw new InvalidDataException("Invalid chamber control namespace.");
        string assembly = ns[(separator + 10)..], typeName = ns[14..separator] + "." + name.LocalName;
        var type = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assembly)?.GetType(typeName);
        if (type is null || !type.IsPublic || type.IsAbstract || !typeof(ModelVisual3D).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidDataException("The declared chamber control is not available in the client.");
        return type;
    }
    private static XmlReaderSettings ReaderSettings() => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 };
}

public sealed record ChamberDiagramContent(ChamberLayoutModel Layout, UserControl View, Viewport3D Viewport, OrthographicCamera Camera, ChamberDiagramRoot3D World,
    IReadOnlyDictionary<string, ChamberDiagramPart3D> Parts);
