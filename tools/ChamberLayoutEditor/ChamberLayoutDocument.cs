using System.Globalization;
using System.Collections.ObjectModel;
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
    private string _component = "";
    public string Component { get => _component; set { if (SetProperty(ref _component, value)) OnPropertyChanged(nameof(Caption)); } }
    public bool HasLift { get; init; } = true;
    public int Nozzles { get; init; } = 2;
    public bool IsArm => Kind == "ArmAssembly";
    public bool IsBase => Kind == "Base";
    public bool HasDeviceBinding => Kind is not ("Base" or "HomeCup");
    public string DisplayName => IsBase ? L10n.Get("chambereditor.base") + (Id == "Base" ? "" : " " + Id) : Id;
    public string Caption => HasDeviceBinding ? Component : DisplayName;
    public string KindTitle => L10n.Get("chambereditor.kind." + Kind);
    private ObservableCollection<ChamberDimension>? _dimensions;
    public ObservableCollection<ChamberDimension> Dimensions
    {
        get
        {
            if (_dimensions is not null) return _dimensions;
            _dimensions = ChamberDimension.Create(Kind, HasLift, Nozzles);
            if (_dimensions.FirstOrDefault(d => d.Key == "ArmLength") is { } length) length.IsEditable = !AutoLength;
            return _dimensions;
        }
    }
    private bool _autoLength = true;
    public bool AutoLength
    {
        get => _autoLength;
        set
        {
            if (!SetProperty(ref _autoLength, value)) return;
            if (Dimensions.FirstOrDefault(d => d.Key == "ArmLength") is { } length) length.IsEditable = !value;
        }
    }
    public double Size(string key) => Dimensions.Single(d => d.Key == key).SafeValue;

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
public sealed class ChamberLayoutDocument : ObservableObject
{
    public string ComponentRoot { get; init; } = "Chamber.Chamber1";
    public List<ChamberPlacement> Parts { get; } = [];
    public ChamberPlacement BasePart => Parts.First(p => p.IsBase);
    // 保留调用入口，实际尺寸统一存放在底盘组件中。
    public double BaseLength { get => BasePart.Size("Length"); set => BasePart.Dimensions.Single(d => d.Key == "Length").Value = value; }
    public double BaseWidth { get => BasePart.Size("Width"); set => BasePart.Dimensions.Single(d => d.Key == "Width").Value = value; }
    public double BaseThickness { get => BasePart.Size("Thickness"); set => BasePart.Dimensions.Single(d => d.Key == "Thickness").Value = value; }

    private static ChamberPlacement CreateBase(double length = 5.8, double width = 4.6, double thickness = 0.12)
    {
        var part = new ChamberPlacement { Id = "Base", Kind = "Base", HasLift = false, Nozzles = 0 };
        part.Dimensions.Single(d => d.Key == "Length").Value = length;
        part.Dimensions.Single(d => d.Key == "Width").Value = width;
        part.Dimensions.Single(d => d.Key == "Thickness").Value = thickness;
        return part;
    }

    public static ChamberLayoutDocument CreateDefault(string root = "Chamber.Chamber1", int arms = 2)
    {
        var document = new ChamberLayoutDocument { ComponentRoot = root };
        document.Parts.Add(CreateBase());
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
            document.Parts.Add(CreateBase());
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
        string? version = (string?)root?.Attribute("Version");
        if (root?.Name != "ChamberLayout" || version is not ("1" or "2"))
            throw new InvalidDataException(L10n.Get("chambereditor.invalid_format"));
        if (root.Attribute("Units") is { } units && units.Value != "Logical")
            throw new InvalidDataException(L10n.Get("chambereditor.invalid_units"));
        var result = new ChamberLayoutDocument { ComponentRoot = Required(root, "ComponentRoot") };
        if (version == "1") result.Parts.Add(CreateBase(Number(root, "BaseLength"), Number(root, "BaseWidth"),
            (double?)root.Attribute("BaseThickness") ?? 0.12));
        foreach (var element in root.Elements())
        {
            var part = new ChamberPlacement { Kind = element.Name.LocalName, Id = Required(element, "Id"),
                Component = element.Name == "Base" ? (string?)element.Attribute("Component") ?? "" : Required(element, "Component"),
                X = Number(element, "X"), Y = Number(element, "Y"), Z = Number(element, "Z"), Yaw = Number(element, "Yaw"),
                HomeAngle = Number(element, "HomeAngle"), HasLift = bool.Parse(Required(element, "HasLift")),
                Nozzles = int.Parse(Required(element, "Nozzles"), CultureInfo.InvariantCulture) };
            part.AutoLength = (bool?)element.Attribute("AutoLength") ?? true;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var size in element.Elements())
            {
                string key = Required(size, "Key");
                var dimension = part.Dimensions.FirstOrDefault(d => d.Key == key);
                if (size.Name != "Size" || dimension is null || !keys.Add(key))
                    throw new InvalidDataException(L10n.Get("chambereditor.invalid_size", part.Id));
                dimension.Value = Number(size, "Value");
            }
            result.Parts.Add(part);
        }
        if (validate) result.Validate();
        return result;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ComponentRoot) || Parts.Count is < 2 or > 41
            || !Parts.Any(p => p.Kind == "Disk") || !Parts.Any(p => p.IsBase))
            throw new InvalidDataException(L10n.Get("chambereditor.invalid_layout"));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disk = Parts.First(p => p.Kind == "Disk");
        foreach (var part in Parts)
        {
            if (!ChamberComponentCatalog.Contains(part.Kind) || string.IsNullOrWhiteSpace(part.Id)
                || (part.HasDeviceBinding && (string.IsNullOrWhiteSpace(part.Component) || !components.Add(part.Component))) || !ids.Add(part.Id)
                || part.Nozzles is < 0 or > 8 || new[] { part.X, part.Y, part.Z }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 10)
                || new[] { part.Yaw, part.HomeAngle }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 360))
                throw new InvalidDataException(L10n.Get("chambereditor.invalid_part", part.Id));
            if (part.Dimensions.Any(d => !d.IsValid)) throw new InvalidDataException(L10n.Get("chambereditor.invalid_size", part.Id));
            if (part.IsArm && Math.Sqrt(Math.Pow(part.X - disk.X, 2) + Math.Pow(part.Z - disk.Z, 2)) < 0.05)
                throw new InvalidDataException(L10n.Get("chambereditor.arm_too_close", part.Id));
        }
    }

    public string ToXml() => new XDocument(new XElement("ChamberLayout", new XAttribute("Version", "2"),
        new XAttribute("Units", "Logical"), new XAttribute("ComponentRoot", ComponentRoot),
        Parts.Select(p => new XElement(p.Kind, new XAttribute("Id", p.Id), new XAttribute("Component", p.Component),
            new XAttribute("X", p.X), new XAttribute("Y", p.Y), new XAttribute("Z", p.Z), new XAttribute("Yaw", p.Yaw),
            new XAttribute("HomeAngle", p.HomeAngle), new XAttribute("HasLift", p.HasLift), new XAttribute("Nozzles", p.Nozzles),
            new XAttribute("AutoLength", p.AutoLength), p.Dimensions.Select(d => new XElement("Size", new XAttribute("Key", d.Key), new XAttribute("Value", d.Value))))))).ToString();

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
