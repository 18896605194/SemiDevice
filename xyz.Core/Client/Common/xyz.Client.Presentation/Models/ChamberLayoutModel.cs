using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace xyz.Client.Presentation.Models;

/// <summary>ToolBuilder Version=3 的纯布局数据，不解释控件类型、XAML 或设备指令。</summary>
public sealed class ChamberLayoutModel
{
    public string ComponentRoot { get; init; } = string.Empty;
    public string ChamberType { get; init; } = string.Empty;
    public List<ChamberPartLayout> Parts { get; } = [];

    public string ToXml() => new XElement("ChamberLayout", new XAttribute("Version", "3"), new XAttribute("Units", "Logical"),
        new XAttribute("ComponentRoot", ComponentRoot), new XAttribute("ChamberType", ChamberType), Parts.Select(p => new XElement(p.Kind,
            new XAttribute("Id", p.Id), new XAttribute("Component", p.Component), new XAttribute("ParentId", p.ParentId),
            new XAttribute("X", p.X), new XAttribute("Y", p.Y), new XAttribute("Z", p.Z), new XAttribute("Yaw", p.Yaw),
            new XAttribute("HomeAngle", p.HomeAngle), new XAttribute("AutoLength", p.AutoLength),
            p.Sizes.Select(s => new XElement("Size", new XAttribute("Key", s.Key), new XAttribute("Value", s.Value)))))).ToString();

    public static ChamberLayoutModel Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != "ChamberLayout" || (string?)root.Attribute("Version") != "3" || (string?)root.Attribute("Units") != "Logical")
            throw new InvalidDataException("Expected a Version=3 logical chamber layout.");
        var result = new ChamberLayoutModel { ComponentRoot = (string?)root.Attribute("ComponentRoot") ?? "",
            ChamberType = (string?)root.Attribute("ChamberType") ?? "" };
        foreach (var node in root.Elements())
        {
            var part = new ChamberPartLayout(node.Name.LocalName, (string?)node.Attribute("Id") ?? "")
            {
                Component = (string?)node.Attribute("Component") ?? "", ParentId = (string?)node.Attribute("ParentId") ?? "",
                X = Number(node, "X"), Y = Number(node, "Y"), Z = Number(node, "Z"), Yaw = Number(node, "Yaw"),
                HomeAngle = Number(node, "HomeAngle"), AutoLength = (bool?)node.Attribute("AutoLength") ?? true
            };
            var seen = new HashSet<string>();
            foreach (var size in node.Elements())
            {
                string key = (string?)size.Attribute("Key") ?? "";
                double value = Number(size, "Value");
                if (size.Name != "Size" || !part.Sizes.ContainsKey(key) || !seen.Add(key) || value < 0 || value == 0 && key != "StreamLength" || value > 20)
                    throw new InvalidDataException("Invalid chamber dimensions.");
                part.Sizes[key] = value;
            }
            result.Parts.Add(part);
        }
        if (result.ComponentRoot.Length == 0 || result.Parts.Count is < 1 or > 512 || result.Parts.Count(p => p.Kind == "Base") != 1
            || result.Parts.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Parts.Count
            || result.Parts.Where(p => p.Component.Length > 0).Select(p => p.Component).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Parts.Count(p => p.Component.Length > 0))
            throw new InvalidDataException("Invalid chamber layout structure.");
        foreach (var part in result.Parts)
        {
            if (part.Id.Length == 0 || new[] { part.X, part.Y, part.Z }.Any(v => Math.Abs(v) > 10)
                || new[] { part.Yaw, part.HomeAngle }.Any(v => Math.Abs(v) > 360)
                || part.Kind is not ("Base" or "HomeCup") && part.Component.Length == 0
                || part.ParentId.Length > 0 && (part.Kind is not ("Lift" or "Nozzle" or "HomeCup") || !result.Parts.Any(p => p.Id == part.ParentId && p.Kind == "Arm")))
                throw new InvalidDataException("Invalid chamber component binding.");
        }
        return result;
    }
    private static double Number(XElement node, string name)
    {
        if (!double.TryParse((string?)node.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            throw new InvalidDataException("Invalid chamber coordinate.");
        return number;
    }
}

public sealed class ChamberPartLayout
{
    public string Kind { get; }
    public string Id { get; }
    public string Component { get; init; } = string.Empty;
    public string ParentId { get; init; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Yaw { get; set; }
    public double HomeAngle { get; set; } = -90;
    public bool AutoLength { get; set; } = true;
    public Dictionary<string, double> Sizes { get; }
    public double Size(string key) => Sizes[key];
    public ChamberPartLayout(string kind, string id)
    {
        Kind = kind; Id = id;
        Sizes = kind switch
        {
            "Base" => new() { ["Length"] = 5.8, ["Width"] = 4.6, ["Thickness"] = 0.12 },
            "Disk" => new() { ["Radius"] = 1.32, ["Thickness"] = 0.045, ["SpindleHeight"] = 0.32 },
            "Bowl" => new() { ["Radius"] = 1.59, ["HeightScale"] = 1 },
            "Door" => new() { ["Width"] = 2.2, ["Height"] = 0.58, ["DepthScale"] = 1 },
            "Lift" => new() { ["WidthScale"] = 1, ["HeightScale"] = 1 },
            "HomeCup" => new() { ["Radius"] = 0.224, ["Height"] = 0.4585 },
            "Nozzle" => new() { ["LengthScale"] = 1, ["Length"] = 2.31625, ["DiameterScale"] = 1, ["StreamLength"] = 0.6 },
            "Arm" => new() { ["ArmLength"] = 2.31625, ["ArmWidth"] = 0.161, ["ArmThickness"] = 0.0595, ["PivotRadius"] = 0.1995 },
            _ => throw new InvalidDataException("Unknown chamber component kind.")
        };
    }
    public void ApplyPlacement(ChamberPartLayout source)
    {
        X = source.X; Y = source.Y; Z = source.Z; Yaw = source.Yaw; HomeAngle = source.HomeAngle; AutoLength = source.AutoLength;
        foreach (var pair in source.Sizes) Sizes[pair.Key] = pair.Value;
    }
}
