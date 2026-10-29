using System.Collections.ObjectModel;
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
    public string ParentId { get; init; } = "";
    public bool HasLift { get; init; }
    public int Nozzles { get; init; }
    public string LiftComponent { get; init; } = "";
    public IReadOnlyList<string> NozzleComponents { get; init; } = [];
    public IEnumerable<string> AttachedComponents => Array.Empty<string>();
    public bool IsArm => Kind == "Arm";
    public bool IsAttachedNozzle => Kind == "Nozzle" && ParentId.Length > 0;
    public bool IsBase => Kind == "Base";
    public bool IsAttached => ParentId.Length > 0;
    public bool HasDeviceBinding => Kind is not ("Base" or "HomeCup");
    public string DisplayName => IsBase ? L10n.Get("chambereditor.base") : Kind == "HomeCup" ? L10n.Get("chambereditor.kind.HomeCup") : Component.Split('.')[^1];
    public string Caption => HasDeviceBinding ? Component : DisplayName;
    public string KindTitle => L10n.Get("chambereditor.kind." + Kind);
    private ObservableCollection<ChamberDimension>? _dimensions;
    public ObservableCollection<ChamberDimension> Dimensions
    {
        get
        {
            if (_dimensions is not null) return _dimensions;
            _dimensions = ChamberDimension.Create(Kind, HasLift, Nozzles);
            if (_dimensions.FirstOrDefault(d => d.Key == AutomaticLengthKey) is { } length) length.IsEditable = !AutoLength;
            return _dimensions;
        }
    }
    private bool _autoLength = true;
    public bool AutoLength
    {
        get => _autoLength;
        set { if (SetProperty(ref _autoLength, value) && Dimensions.FirstOrDefault(d => d.Key == AutomaticLengthKey) is { } length) length.IsEditable = !value; }
    }
    private string AutomaticLengthKey => IsArm ? "ArmLength" : IsAttachedNozzle ? "Length" : "";
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

public sealed class ChamberLayoutDocument : ObservableObject
{
    public string ComponentRoot { get; init; } = "";
    public string ChamberType { get; init; } = "";
    public string ModuleClass { get; init; } = "";
    public string Title => ChamberType.Length == 0 ? ComponentRoot : $"{ComponentRoot} ({ChamberType})";
    public string PickerTitle => ChamberType.Length == 0 ? ComponentRoot.Split('.')[^1] : $"{ComponentRoot.Split('.')[^1]} ({ChamberType})";
    public List<ChamberPlacement> Parts { get; } = [];
    public ChamberPlacement BasePart => Parts.First(p => p.IsBase);
    public double BaseLength { get => BasePart.Size("Length"); set => BasePart.Dimensions.Single(d => d.Key == "Length").Value = value; }
    public double BaseWidth { get => BasePart.Size("Width"); set => BasePart.Dimensions.Single(d => d.Key == "Width").Value = value; }
    public double BaseThickness { get => BasePart.Size("Thickness"); set => BasePart.Dimensions.Single(d => d.Key == "Thickness").Value = value; }

    public static IReadOnlyList<ChamberLayoutDocument> ReadMachine(string xml, Func<string, string?>? resolveKind = null)
    {
        if (resolveKind is null) throw new InvalidDataException(L10n.Get("chambereditor.metadata_required"));
        var source = ReadXml(xml);
        string Role(XElement e) => resolveKind((string?)e.Attribute("Type") ?? "") ?? "";
        static IEnumerable<XElement> Children(XElement parent) => parent.Elements("Setting").SelectMany(e =>
            string.IsNullOrWhiteSpace((string?)e.Attribute("Type")) ? Children(e) : new[] { e });
        var documents = new List<ChamberLayoutDocument>();
        foreach (var chamber in source.Root?.DescendantsAndSelf("Setting").Where(e => Role(e) == "Chamber") ?? [])
        {
            string PathOf(XElement e) => string.Join('.', e.AncestorsAndSelf("Setting").TakeWhile(p => p != chamber).Reverse().Select(p => Required(p, "Name")));
            var document = new ChamberLayoutDocument
            {
                ComponentRoot = string.Join('.', chamber.AncestorsAndSelf("Setting").Reverse().Select(p => Required(p, "Name"))),
                ChamberType = (string?)chamber.Elements("Value").FirstOrDefault(v => (string?)v.Attribute("Name") == "Type")?.Attribute("Value") ?? "",
                ModuleClass = Required(chamber, "Type")
            };
            document.Parts.Add(new() { Id = "$base", Kind = "Base" });
            int armIndex = 0, bowlIndex = 0, otherIndex = 0;
            foreach (var node in Children(chamber))
            {
                string kind = Role(node), id = PathOf(node);
                if (!ChamberComponentCatalog.Contains(kind) || kind is "Base" or "HomeCup") continue;
                var children = Children(node).ToArray();
                var nozzles = kind == "Arm" ? children.Where(e => Role(e) == "Nozzle").ToArray() : [];
                string lift = kind == "Arm" ? children.Where(e => Role(e) == "Lift").Select(PathOf).SingleOrDefault() ?? "" : "";
                var part = new ChamberPlacement
                {
                    Id = id, Component = id, Kind = kind, HasLift = lift.Length > 0, LiftComponent = lift,
                    Nozzles = nozzles.Length, NozzleComponents = nozzles.Select(PathOf).ToArray(),
                    X = kind == "Arm" ? (armIndex % 2 == 0 ? 1.95 : -1.95) : kind is "Nozzle" or "Lift" ? 2.6 : 0,
                    Y = kind == "Disk" ? 0.32 : kind == "Nozzle" ? 1 : 0,
                    Z = kind == "Arm" ? -1.25 + armIndex / 2 * 0.6 : kind == "Door" ? 2.42 : kind is "Nozzle" or "Lift" ? -1 + otherIndex++ * 0.6 : 0
                };
                document.Parts.Add(part);
                if (kind == "Bowl")
                {
                    part.Dimensions.Single(d => d.Key == "Radius").Value = 1.59 + bowlIndex * 0.18;
                    part.Dimensions.Single(d => d.Key == "HeightScale").Value = 1 + bowlIndex++ * 0.3;
                }
                if (!part.IsArm) continue;
                armIndex++;
                foreach (var child in children.Where(e => Role(e) is "Lift" or "Nozzle"))
                {
                    string role = Role(child), path = PathOf(child);
                    int nozzleIndex = Array.IndexOf(nozzles, child);
                    document.Parts.Add(new()
                    {
                        Id = path, Component = path, ParentId = id, Kind = role,
                        Y = role == "Nozzle" ? 0.07 : 0,
                        Z = role == "Nozzle" ? (nozzleIndex - (nozzles.Length - 1) / 2d) * 0.105 : 0
                    });
                }
                if (nozzles.Length > 0) document.Parts.Add(new() { Id = id + ".$cup", Kind = "HomeCup", ParentId = id });
            }
            document.Validate(); documents.Add(document);
        }
        return documents;
    }

    public ChamberLayoutDocument WithLayout(ChamberLayoutDocument layout)
    {
        if (layout.ComponentRoot != ComponentRoot) throw new InvalidDataException(L10n.Get("chambereditor.layout_chamber_mismatch", layout.ComponentRoot));
        var result = Parse(ToXml());
        foreach (var part in result.Parts)
        {
            var previous = layout.Parts.FirstOrDefault(p => p.Kind == part.Kind && (part.HasDeviceBinding ? p.Component == part.Component : p.Id == part.Id));
            if (previous is null || previous.ParentId != part.ParentId) continue;
            part.X = previous.X; part.Y = previous.Y; part.Z = previous.Z; part.Yaw = previous.Yaw;
            part.HomeAngle = previous.HomeAngle; part.AutoLength = previous.AutoLength;
            foreach (var dimension in part.Dimensions)
                if (previous.Dimensions.FirstOrDefault(d => d.Key == dimension.Key) is { } old) dimension.Value = old.Value;
        }
        result.Validate(); return result;
    }

    public static ChamberLayoutDocument Parse(string xml) => ParseSnapshot(xml, true);
    internal static ChamberLayoutDocument ParseSnapshot(string xml, bool validate = false)
    {
        var root = ReadXml(xml).Root;
        if (root?.Name != "ChamberLayout" || (string?)root.Attribute("Version") != "3") throw new InvalidDataException(L10n.Get("chambereditor.invalid_format"));
        if ((string?)root.Attribute("Units") != "Logical") throw new InvalidDataException(L10n.Get("chambereditor.invalid_units"));
        var result = new ChamberLayoutDocument { ComponentRoot = Required(root, "ComponentRoot"),
            ChamberType = (string?)root.Attribute("ChamberType") ?? "", ModuleClass = (string?)root.Attribute("ModuleClass") ?? "" };
        foreach (var element in root.Elements())
        {
            var part = new ChamberPlacement
            {
                Id = Required(element, "Id"), Kind = element.Name.LocalName, Component = (string?)element.Attribute("Component") ?? "",
                ParentId = (string?)element.Attribute("ParentId") ?? "", HasLift = (bool?)element.Attribute("HasLift") ?? false,
                LiftComponent = (string?)element.Attribute("LiftComponent") ?? "", Nozzles = (int?)element.Attribute("Nozzles") ?? 0,
                X = Number(element, "X"), Y = Number(element, "Y"), Z = Number(element, "Z"), Yaw = Number(element, "Yaw"),
                HomeAngle = Number(element, "HomeAngle"), AutoLength = (bool?)element.Attribute("AutoLength") ?? true
            };
            var keys = new HashSet<string>();
            foreach (var size in element.Elements())
            {
                string key = Required(size, "Key");
                var dimension = part.Dimensions.FirstOrDefault(d => d.Key == key);
                if (size.Name != "Size" || dimension is null || !keys.Add(key)) throw new InvalidDataException(L10n.Get("chambereditor.invalid_size", part.Id));
                dimension.Value = Number(size, "Value");
            }
            result.Parts.Add(part);
        }
        if (validate) result.Validate(); return result;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ComponentRoot) || Parts.Count is < 1 or > 512 || Parts.Count(p => p.IsBase) != 1)
            throw new InvalidDataException(L10n.Get("chambereditor.invalid_layout"));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disk = Parts.FirstOrDefault(p => p.Kind == "Disk");
        foreach (var part in Parts)
        {
            if (!ChamberComponentCatalog.Contains(part.Kind) || string.IsNullOrWhiteSpace(part.Id) || !ids.Add(part.Id)
                || part.HasDeviceBinding && (string.IsNullOrWhiteSpace(part.Component) || !components.Add(part.Component))
                || new[] { part.X, part.Y, part.Z }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 10)
                || new[] { part.Yaw, part.HomeAngle }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 360)
                || part.ParentId.Length > 0 && (!Parts.Any(p => p.Id == part.ParentId && p.IsArm) || part.Kind is not ("Lift" or "Nozzle" or "HomeCup")))
                throw new InvalidDataException(L10n.Get("chambereditor.invalid_part", part.Id));
            if (part.Dimensions.Any(d => !d.IsValid)) throw new InvalidDataException(L10n.Get("chambereditor.invalid_size", part.Id));
            if (part.IsArm && disk is not null && Math.Sqrt(Math.Pow(part.X - disk.X, 2) + Math.Pow(part.Z - disk.Z, 2)) < 0.05)
                throw new InvalidDataException(L10n.Get("chambereditor.arm_too_close", part.Id));
        }
    }

    public string ToXml() => new XDocument(new XElement("ChamberLayout", new XAttribute("Version", "3"), new XAttribute("Units", "Logical"),
        new XAttribute("ComponentRoot", ComponentRoot), new XAttribute("ChamberType", ChamberType), new XAttribute("ModuleClass", ModuleClass),
        Parts.Select(p => new XElement(p.Kind, new XAttribute("Id", p.Id), new XAttribute("Component", p.Component), new XAttribute("ParentId", p.ParentId),
            new XAttribute("X", p.X), new XAttribute("Y", p.Y), new XAttribute("Z", p.Z), new XAttribute("Yaw", p.Yaw), new XAttribute("HomeAngle", p.HomeAngle),
            new XAttribute("AutoLength", p.AutoLength), new XAttribute("HasLift", p.HasLift), new XAttribute("LiftComponent", p.LiftComponent), new XAttribute("Nozzles", p.Nozzles),
            p.Dimensions.Select(d => new XElement("Size", new XAttribute("Key", d.Key), new XAttribute("Value", d.Value))))))).ToString();
    internal static XDocument ReadXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 });
        return XDocument.Load(reader);
    }
    private static string Required(XElement e, string key) => (string?)e.Attribute(key) ?? throw new InvalidDataException(L10n.Get("chambereditor.invalid_format"));
    private static double Number(XElement e, string key) => double.Parse(Required(e, key), CultureInfo.InvariantCulture);
}
