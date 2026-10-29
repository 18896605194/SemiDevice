using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;

namespace ChamberLayoutEditor;

public sealed class ChamberPlacement : ObservableObject
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Component { get; init; } = "";
    public bool HasLift { get; init; } = true;
    public int Nozzles { get; init; } = 2;
    public bool IsArm => Kind == "ArmAssembly";
    public string KindTitle => L10n.Get("chambereditor.kind." + Kind);

    private double _x;
    public double X { get => _x; set => SetProperty(ref _x, value); }
    private double _y;
    public double Y { get => _y; set => SetProperty(ref _y, value); }
    private double _z;
    public double Z { get => _z; set => SetProperty(ref _z, value); }
    private double _yaw;
    public double Yaw { get => _yaw; set => SetProperty(ref _yaw, value); }
    private double _homeAngle = -90;
    public double HomeAngle { get => _homeAngle; set => SetProperty(ref _homeAngle, value); }
}

/// <summary>离线示意布局：坐标为场景单位，设备引用相对于 ComponentRoot，不包含控制指令。</summary>
public sealed class ChamberLayoutDocument
{
    public string ComponentRoot { get; init; } = "Chamber.Chamber1";
    public double BaseLength { get; init; } = 5.8;
    public double BaseWidth { get; init; } = 4.6;
    public List<ChamberPlacement> Parts { get; } = [];

    public static ChamberLayoutDocument CreateDefault(string root = "Chamber.Chamber1", int arms = 2)
    {
        var document = new ChamberLayoutDocument { ComponentRoot = root };
        document.Parts.Add(new() { Id = "SpinMotor", Kind = "Disk", Component = "SpinMotor", Y = 0.32 });
        document.Parts.Add(new() { Id = "Bowl1", Kind = "Bowl", Component = "Bowl1" });
        document.Parts.Add(new() { Id = "Door", Kind = "Door", Component = "Door", Z = 2.42 });
        for (int i = 0; i < arms; i++)
            document.Parts.Add(new() { Id = $"Arm{i + 1}", Kind = "ArmAssembly", Component = $"Arm{i + 1}",
                X = i % 2 == 0 ? 1.95 : -1.95, Z = -1.25 + i / 2 * 0.6 });
        return document;
    }

    public static IReadOnlyList<ChamberLayoutDocument> ReadMachine(string xml)
    {
        var source = ReadXml(xml);
        var documents = new List<ChamberLayoutDocument>();
        foreach (var chamber in source.Descendants("Setting").Where(e =>
                     ((string?)e.Attribute("Type"))?.EndsWith(".ChamberModule", StringComparison.Ordinal) == true))
        {
            string root = string.Join('.', chamber.AncestorsAndSelf("Setting").Reverse().Select(e => (string?)e.Attribute("Name")));
            var document = new ChamberLayoutDocument { ComponentRoot = root };
            int arms = 0;
            foreach (var item in chamber.Elements("Setting"))
            {
                string type = (string?)item.Attribute("Type") ?? "";
                string name = (string?)item.Attribute("Name") ?? "";
                string kind = type.EndsWith(".SwingArmElement", StringComparison.Ordinal) ? "ArmAssembly" :
                    type.EndsWith(".SpinMotorElement", StringComparison.Ordinal) ? "Disk" :
                    type.EndsWith(".ChamberBowlElement", StringComparison.Ordinal) ? "Bowl" :
                    type.EndsWith(".ChamberDoorElement", StringComparison.Ordinal) ? "Door" : "";
                if (kind.Length == 0) continue;
                var part = new ChamberPlacement { Id = name, Component = name, Kind = kind,
                    HasLift = item.Elements("Setting").Any(e => (string?)e.Attribute("Name") == "Lift"),
                    Nozzles = item.Elements("Setting").Count(e => ((string?)e.Attribute("Type"))?.EndsWith(".NozzleElement", StringComparison.Ordinal) == true),
                    X = kind == "ArmAssembly" ? (arms % 2 == 0 ? 1.95 : -1.95) : 0,
                    Y = kind == "Disk" ? 0.32 : 0,
                    Z = kind == "ArmAssembly" ? -1.25 + arms / 2 * 0.6 : kind == "Door" ? 2.42 : 0 };
                if (part.IsArm) arms++;
                document.Parts.Add(part);
            }
            document.Validate();
            documents.Add(document);
        }
        if (documents.Count == 0) throw new InvalidDataException(L10n.Get("chambereditor.no_chamber"));
        return documents;
    }

    public static ChamberLayoutDocument Parse(string xml) => ParseSnapshot(xml, true);

    internal static ChamberLayoutDocument ParseSnapshot(string xml, bool validate = false)
    {
        var root = ReadXml(xml).Root;
        if (root?.Name != "ChamberLayout" || (string?)root.Attribute("Version") != "1")
            throw new InvalidDataException(L10n.Get("chambereditor.invalid_format"));
        var result = new ChamberLayoutDocument { ComponentRoot = Required(root, "ComponentRoot"),
            BaseLength = Number(root, "BaseLength"), BaseWidth = Number(root, "BaseWidth") };
        foreach (var element in root.Elements())
            result.Parts.Add(new() { Kind = element.Name.LocalName, Id = Required(element, "Id"), Component = Required(element, "Component"),
                X = Number(element, "X"), Y = Number(element, "Y"), Z = Number(element, "Z"), Yaw = Number(element, "Yaw"),
                HomeAngle = Number(element, "HomeAngle"), HasLift = bool.Parse(Required(element, "HasLift")),
                Nozzles = int.Parse(Required(element, "Nozzles"), CultureInfo.InvariantCulture) });
        if (validate) result.Validate();
        return result;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ComponentRoot) || !double.IsFinite(BaseLength) || !double.IsFinite(BaseWidth)
            || BaseLength is < 1 or > 20 || BaseWidth is < 1 or > 20 || Parts.Count is < 1 or > 40
            || Parts.Count(p => p.Kind == "Disk") != 1)
            throw new InvalidDataException(L10n.Get("chambereditor.invalid_layout"));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disk = Parts.Single(p => p.Kind == "Disk");
        foreach (var part in Parts)
        {
            if (part.Kind is not ("Disk" or "Bowl" or "Door" or "ArmAssembly") || string.IsNullOrWhiteSpace(part.Id)
                || string.IsNullOrWhiteSpace(part.Component) || !ids.Add(part.Id) || !components.Add(part.Component)
                || part.Nozzles is < 0 or > 8 || new[] { part.X, part.Y, part.Z }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 10)
                || new[] { part.Yaw, part.HomeAngle }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 360))
                throw new InvalidDataException(L10n.Get("chambereditor.invalid_part", part.Id));
            if (part.IsArm && Math.Sqrt(Math.Pow(part.X - disk.X, 2) + Math.Pow(part.Z - disk.Z, 2)) < 0.8)
                throw new InvalidDataException(L10n.Get("chambereditor.arm_too_close", part.Id));
        }
    }

    public string ToXml() => new XDocument(new XElement("ChamberLayout", new XAttribute("Version", "1"),
        new XAttribute("ComponentRoot", ComponentRoot), new XAttribute("BaseLength", BaseLength), new XAttribute("BaseWidth", BaseWidth),
        Parts.Select(p => new XElement(p.Kind, new XAttribute("Id", p.Id), new XAttribute("Component", p.Component),
            new XAttribute("X", p.X), new XAttribute("Y", p.Y), new XAttribute("Z", p.Z), new XAttribute("Yaw", p.Yaw),
            new XAttribute("HomeAngle", p.HomeAngle), new XAttribute("HasLift", p.HasLift), new XAttribute("Nozzles", p.Nozzles))))).ToString();

    private static XDocument ReadXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 });
        return XDocument.Load(reader);
    }
    private static string Required(XElement e, string name) => (string?)e.Attribute(name)
        ?? throw new InvalidDataException(L10n.Get("chambereditor.invalid_format"));
    private static double Number(XElement e, string name) => double.Parse(Required(e, name), CultureInfo.InvariantCulture);
}
