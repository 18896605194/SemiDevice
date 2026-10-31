using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;
using ChamberLayoutEditor;
using ToolBuilder.Core;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;

namespace ToolBuilder;

public sealed partial class MainViewModel
{
    public ChamberEditorPage ChamberEditor { get; private set; } = null!;
    private readonly Dictionary<XElement, ChamberRecord> _chamberLayouts = [];
    private readonly Dictionary<string, FileWrite> _chamberFiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _syncingChamber;
    private bool _chamberRefreshQueued;
    private string _chamberSchemaXml = "";
    private string _chamberLayoutError = "";
    public string ChamberLayoutError { get => _chamberLayoutError; private set => Set(ref _chamberLayoutError, value); }
    public bool CanEditChamberLayout => FindChamber(Selected?.Element) is not null;
    public bool HasChamberChanges => ChamberEditor?.HasUnsavedChanges == true || _chamberLayouts.Values.Any(r => r.IsDirty);
    public string ChamberTitle => ChamberEditor.ViewModel.Document?.Title ?? "";

    private void InitializeChamberEditor()
    {
        ChamberEditor = new ChamberEditorPage(); ChamberEditor.ConfigureEmbedded();
        ChamberEditor.DocumentChanged += () => { if (!_syncingChamber) Raise(nameof(Title)); };
        ChamberEditor.SaveRequested += () => SaveCommand.Execute(null);
        ChamberEditor.ViewModel.PropertyChanged += (_, e) =>
        {
            if (!_syncingChamber && e.PropertyName == nameof(ChamberEditorViewModel.Selected)) SelectChamberComponentInTree();
        };
    }
    private string? ChamberRole(string type) => _catalog.Elements.GetValueOrDefault(type)?.ChamberRole;
    private void RefreshChamberVisualCatalog()
    {
        if (_document is null) return;
        var files = ClientVisualSourceDiscovery.Find(_document.FilePath, Sources);
        ChamberEditor.SetVisualCatalog(new ChamberVisualCatalog(ChamberVisualProviderLoader.Load(files)));
    }
    private XElement? FindChamber(XElement? element) => element?.AncestorsAndSelf("Setting").FirstOrDefault(e => ChamberRole(ConfigDocument.Attribute(e, "Type")) == "Chamber");
    internal static string ChamberLayoutPath(string machinePath, string root) => Path.Combine(Path.GetDirectoryName(machinePath)!, "ChamberLayouts",
        Path.GetFileNameWithoutExtension(machinePath), Uri.EscapeDataString(root) + ".xml");

    private void ResetChamberLayouts(string machinePath)
    {
        var files = new Dictionary<string, FileWrite>(StringComparer.OrdinalIgnoreCase);
        string folder = Path.GetDirectoryName(ChamberLayoutPath(machinePath, "placeholder"))!;
        if (Directory.Exists(folder)) foreach (string path in Directory.EnumerateFiles(folder).Where(p => Path.GetExtension(p) is ".xml" or ".xaml"))
        {
            byte[] bytes = File.ReadAllBytes(path); files[path] = new(path, bytes, SHA256.HashData(bytes));
        }
        _syncingChamber = true;
        try
        {
            _chamberLayouts.Clear(); _chamberFiles.Clear();
            _chamberSchemaXml = "";
            foreach (var pair in files) _chamberFiles.Add(pair.Key, pair.Value);
            ChamberEditor.ViewModel.Clear(); ChamberLayoutError = "";
        }
        finally { _syncingChamber = false; }
    }
    private void QueueChamberRefresh()
    {
        if (_chamberRefreshQueued || _syncingChamber) return;
        _chamberRefreshQueued = true;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _chamberRefreshQueued = false; RefreshChamberLayouts();
            if (EditorPage == "ChamberLayout") OpenChamberLayout();
        }, DispatcherPriority.Background);
    }
    private void CaptureChamberLayouts()
    {
        if (!ChamberEditor.ViewModel.HasConfiguration || _syncingChamber) return;
        foreach (var element in XDocument.Parse(ChamberEditor.ViewModel.PrepareWorkspaceSave()).Root!.Elements("ChamberLayout"))
        {
            string root = (string)element.Attribute("ComponentRoot")!;
            var record = _chamberLayouts.Values.FirstOrDefault(r => r.Root == root);
            if (record is not null) record.Xml = element.ToString();
        }
    }
    private void RefreshChamberLayouts()
    {
        if (_document is null || _syncingChamber) return;
        try
        {
            var configurations = ChamberLayoutDocument.ReadMachine(_document.GetText(), ChamberRole);
            string schema = string.Join("\n", configurations.Select(c => c.ToXml()));
            if (schema == _chamberSchemaXml) { ChamberLayoutError = ""; return; }
            CaptureChamberLayouts();
            var nodes = _document.Settings.Where(e => ChamberRole(ConfigDocument.Attribute(e, "Type")) == "Chamber").ToDictionary(ConfigDocument.PathOf);
            var layouts = new List<ChamberLayoutDocument>();
            foreach (var configuration in configurations)
            {
                var node = nodes[configuration.ComponentRoot];
                if (!_chamberLayouts.TryGetValue(node, out var record))
                {
                    string path = ChamberLayoutPath(_document.FilePath, configuration.ComponentRoot);
                    byte[]? bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
                    record = new ChamberRecord(node, configuration, path, bytes); _chamberLayouts.Add(node, record);
                }
                else record.Reconcile(configuration);
                layouts.Add(ChamberLayoutDocument.Parse(record.Xml));
            }
            _syncingChamber = true;
            string? selectedRoot = ChamberEditor.ViewModel.SelectedChamber?.ComponentRoot;
            if (layouts.Count == 0) ChamberEditor.ViewModel.Clear();
            else
            {
                ChamberEditor.ViewModel.LoadChambers(layouts);
                if (ChamberEditor.ViewModel.Chambers.FirstOrDefault(c => c.ComponentRoot == selectedRoot) is { } selected)
                    ChamberEditor.ViewModel.SelectedChamber = selected;
            }
            ChamberLayoutError = "";
            _chamberSchemaXml = schema;
        }
        catch (Exception exception) { ChamberLayoutError = exception.Message; }
        finally
        {
            _syncingChamber = false;
            Raise(nameof(CanEditChamberLayout)); Raise(nameof(ChamberTitle)); Raise(nameof(Title));
        }
    }
    private void OpenChamberLayout()
    {
        var chamber = FindChamber(Selected?.Element);
        if (chamber is null) return;
        if (!_chamberLayouts.ContainsKey(chamber)) RefreshChamberLayouts();
        var target = ChamberEditor.ViewModel.Chambers.FirstOrDefault(c => c.ComponentRoot == ConfigDocument.PathOf(chamber));
        if (target is null) return;
        _syncingChamber = true;
        try
        {
            ChamberEditor.ViewModel.SelectedChamber = target; _chamberLayouts[chamber].Accessed = true;
            string root = target.ComponentRoot;
            var part = ChamberEditor.ViewModel.Parts.Where(p => p.HasDeviceBinding).OrderByDescending(p => p.Component.Length)
                .FirstOrDefault(p => SelectionPath == root + "." + p.Component || SelectionPath.StartsWith(root + "." + p.Component + ".", StringComparison.Ordinal));
            if (part is not null) ChamberEditor.ViewModel.Selected = part;
        }
        finally { _syncingChamber = false; }
        Raise(nameof(ChamberTitle)); Raise(nameof(Title));
    }
    private void SelectChamberComponentInTree()
    {
        var model = ChamberEditor.ViewModel;
        if (model.Selected is not { HasDeviceBinding: true } part || model.Document is null) return;
        string path = model.Document.ComponentRoot + "." + part.Component;
        var node = Roots.SelectMany(r => r.Walk()).FirstOrDefault(n => n.Path == path);
        if (node is null || ReferenceEquals(node, Selected)) return;
        _syncingChamber = true;
        try { SelectNode(node); }
        finally { _syncingChamber = false; }
    }
    private void ValidateChamberLayouts()
    {
        if (_document is null) return;
        try { CaptureChamberLayouts(); }
        catch (Exception failure) { Issues.Add(new("错误", SelectionPath, failure.Message)); return; }
        RefreshChamberLayouts();
        if (ChamberLayoutError.Length > 0) Issues.Add(new("错误", SelectionPath, ChamberLayoutError));
        foreach (var record in _chamberLayouts.Values)
        {
            if (!record.Node.Ancestors().Contains(_document.Xml.Root!))
            {
                if (record.IsDirty) Issues.Add(new("错误", record.Root, "已删除的腔体还有未保存布局，请先撤回删除或重新打开配置。"));
                continue;
            }
            try { ChamberLayoutDocument.Parse(record.Xml).Validate(); }
            catch (Exception ex) { Issues.Add(new("错误", record.Root, ex.Message)); }
        }
    }
    private List<(ChamberRecord Record, FileWrite Write, FileWrite DiagramWrite)> PrepareChamberWrites(string target)
    {
        RefreshChamberLayouts(); CaptureChamberLayouts();
        if (ChamberLayoutError.Length > 0) throw new InvalidDataException(ChamberLayoutError);
        var writes = new List<(ChamberRecord, FileWrite, FileWrite)>();
        foreach (var record in _chamberLayouts.Values)
        {
            if (!record.Node.Ancestors().Contains(_document!.Xml.Root!))
            {
                if (record.IsDirty) throw new InvalidDataException("已删除的腔体还有未保存布局。");
                continue;
            }
            string path = ChamberLayoutPath(target, record.Root);
            string xml = ChamberLayoutDocument.Parse(record.Xml).ToXml();
            string diagramPath = Path.ChangeExtension(path, ".xaml");
            writes.Add((record, new FileWrite(path, Encoding.UTF8.GetBytes(xml),
                string.Equals(path, record.Path, StringComparison.OrdinalIgnoreCase) ? record.Fingerprint : null),
                new FileWrite(diagramPath, Encoding.UTF8.GetBytes(ChamberDiagramXaml.Export(ChamberLayoutModel.Parse(xml), ChamberEditor.VisualCatalog)),
                    string.Equals(diagramPath, record.DiagramPath, StringComparison.OrdinalIgnoreCase) ? record.DiagramFingerprint : null)));
        }
        return writes;
    }
    private void AppendUnopenedChamberWrites(List<FileWrite> writes, string target)
    {
        string folder = Path.GetDirectoryName(ChamberLayoutPath(target, "placeholder"))!;
        foreach (var original in _chamberFiles.Values)
        {
            string destination = Path.Combine(folder, Path.GetFileName(original.Path));
            if (writes.Any(w => string.Equals(w.Path, destination, StringComparison.OrdinalIgnoreCase))) continue;
            if (!File.Exists(original.Path) || !SHA256.HashData(File.ReadAllBytes(original.Path)).SequenceEqual(original.ExpectedHash!))
                throw new IOException("腔体布局已被外部修改，请重新打开后另存为：" + original.Path);
            writes.Add(new(destination, original.Content, null));
        }
    }
    private void AcceptChamberWrites(IEnumerable<(ChamberRecord Record, FileWrite Write, FileWrite DiagramWrite)> writes, bool moving, IReadOnlyList<FileWrite> allWrites)
    {
        if (moving) _chamberFiles.Clear();
        foreach (var (record, write, diagram) in writes)
        {
            record.Path = write.Path; record.Fingerprint = SHA256.HashData(write.Content); record.SavedXml = record.Xml;
            record.DiagramPath = diagram.Path; record.DiagramFingerprint = SHA256.HashData(diagram.Content);
            _chamberFiles[write.Path] = write with { ExpectedHash = record.Fingerprint };
        }
        string folder = Path.GetDirectoryName(ChamberLayoutPath(allWrites[0].Path, "placeholder"))! + Path.DirectorySeparatorChar;
        foreach (var write in allWrites.Where(w => w.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            _chamberFiles[write.Path] = write with { ExpectedHash = SHA256.HashData(write.Content) };
        ChamberEditor.ViewModel.MarkWorkspaceSaved(); Raise(nameof(Title));
    }
    private sealed class ChamberRecord
    {
        public XElement Node { get; }
        public string Root { get; private set; }
        public string Xml { get; set; }
        public string SavedXml { get; set; }
        public string Path { get; set; }
        public byte[]? Fingerprint { get; set; }
        public string DiagramPath { get; set; }
        public byte[]? DiagramFingerprint { get; set; }
        public bool Accessed { get; set; }
        public bool IsDirty => Xml != SavedXml || Accessed && (Fingerprint is null || DiagramFingerprint is null);
        private Dictionary<XElement, string> _bindings;
        public ChamberRecord(XElement node, ChamberLayoutDocument configuration, string path, byte[]? bytes)
        {
            Node = node; Root = configuration.ComponentRoot; Path = path;
            DiagramPath = System.IO.Path.ChangeExtension(path, ".xaml");
            DiagramFingerprint = File.Exists(DiagramPath) ? SHA256.HashData(File.ReadAllBytes(DiagramPath)) : null;
            var previous = bytes is null ? configuration : ChamberLayoutDocument.Parse(Encoding.UTF8.GetString(bytes));
            SavedXml = previous.ToXml(); Xml = configuration.WithLayout(previous).ToXml();
            Fingerprint = bytes is null ? null : SHA256.HashData(bytes); _bindings = Bindings();
        }
        private Dictionary<XElement, string> Bindings() => Node.DescendantsAndSelf("Setting").ToDictionary(e => e,
            e => e == Node ? "" : ConfigDocument.PathOf(e)[(ConfigDocument.PathOf(Node).Length + 1)..]);
        public void Reconcile(ChamberLayoutDocument configuration)
        {
            var current = Bindings();
            string Remap(string path)
            {
                foreach (var pair in _bindings.Where(p => p.Value.Length > 0 && current.ContainsKey(p.Key)).OrderByDescending(p => p.Value.Length))
                    if (path == pair.Value || path.StartsWith(pair.Value + ".", StringComparison.Ordinal)) return current[pair.Key] + path[pair.Value.Length..];
                return path;
            }
            var xml = XDocument.Parse(Xml); xml.Root!.SetAttributeValue("ComponentRoot", configuration.ComponentRoot);
            foreach (var part in xml.Root.Elements()) foreach (string key in new[] { "Id", "Component", "ParentId", "LiftComponent" })
                if (part.Attribute(key) is { } attribute) attribute.Value = Remap(attribute.Value);
            Xml = configuration.WithLayout(ChamberLayoutDocument.Parse(xml.ToString())).ToXml(); Root = configuration.ComponentRoot; _bindings = current;
        }
    }
}
