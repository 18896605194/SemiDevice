using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CdsAssemblyPreview;
using ToolBuilder.Core;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;

namespace ToolBuilder;

/// <summary>一个药液模块的工具编辑会话，统一保存由宿主管理。</summary>
public sealed class CdsLayoutViewModel : BaseViewModel
{
    #region Column
    public PreviewPage Editor { get; }
    public string Description => L10n.Get("cdsp.workspace.description", _module?.Path ?? "", Editor.ViewModel.TankCount, Editor.ViewModel.ValveCount, Editor.ViewModel.SensorCount);
    public string FilePath => _path;
    public string DiagramPath => Path.ChangeExtension(_path, ".xaml");
    public bool IsDirty => _isNew || Editor.ViewModel.Layout.ToXml() != _savedXml;
    public event Action? Changed;
    #endregion
    #region Service
    private NodeModel? _module;
    private string _machinePath = "", _path = "", _savedXml = "", _schema = "";
    private byte[]? _fingerprint, _diagramFingerprint;
    private bool _isNew, _initialized, _loading;
    private Func<string, string?>? _resolve;
    private Dictionary<XElement, string> _bindings = [];
    #endregion
    public CdsLayoutViewModel(PreviewPage editor)
    {
        Editor = editor;
        Editor.ViewModel.LayoutChanged += () => { if (!_loading) Changed?.Invoke(); };
    }
    public void Configure(NodeModel module, string machinePath, Func<string, string?> resolve)
    {
        _module = module; _machinePath = machinePath; _resolve = resolve;
    }
    public override void Init()
    {
        if (_initialized || _module is null) return;
        _path = LayoutPath(_machinePath, _module.Path); _isNew = !File.Exists(_path);
        byte[]? bytes = _isNew ? null : File.ReadAllBytes(_path);
        _diagramFingerprint = File.Exists(DiagramPath) ? SHA256.HashData(File.ReadAllBytes(DiagramPath)) : null;
        var saved = bytes is null ? null : CdsLayout.Parse(Encoding.UTF8.GetString(bytes));
        var snapshot = Read();
        _loading = true;
        try
        {
            Editor.ViewModel.Init(); Editor.ConfigureEmbedded(); Editor.ViewModel.LoadConfiguration(snapshot, saved);
            _savedXml = saved?.ToXml() ?? Editor.ViewModel.Layout.ToXml(); _schema = Schema(snapshot); _bindings = Bindings();
            _fingerprint = bytes is null ? null : SHA256.HashData(bytes); _initialized = true;
        }
        finally { _loading = false; }
    }
    public static bool Supports(NodeModel? node, Func<string, string?>? resolve = null)
    {
        if (node is null || node.Type.Length == 0) return false;
        string? role = resolve?.Invoke(node.Type) ?? CdsConfigurationReader.Role(node.Type);
        return role == "Module" || role is null && node.Walk().Skip(1).Any(n => (resolve?.Invoke(n.Type) ?? CdsConfigurationReader.Role(n.Type)) == "Tank");
    }
    public static string LayoutPath(string machinePath, string modulePath) => Path.Combine(Path.GetDirectoryName(machinePath)!, "CdsLayouts",
        Path.GetFileNameWithoutExtension(machinePath), Uri.EscapeDataString(modulePath) + ".xml");
    private CdsSnapshot Read() => CdsConfigurationReader.Read(_module!.Element, _resolve);
    private static string Schema(CdsSnapshot snapshot) => snapshot.ComponentRoot + "\n" + string.Join("\n", snapshot.Tanks.Select(t => "Tank:" + t.Data.Path)
        .Concat(snapshot.Devices.Select(d => d.Kind + ":" + d.Path + ":" + d.VisualKind)));
    private Dictionary<XElement, string> Bindings() => _module!.Element.Descendants("Setting").ToDictionary(e => e,
        e => CdsLayoutComposer.Relative(_module.Path, ConfigDocument.PathOf(e)));
    public void RefreshConfiguration()
    {
        var snapshot = Read(); if (Schema(snapshot) == _schema) return;
        var current = Bindings();
        string Remap(string path)
        {
            foreach (var old in _bindings.Where(p => current.ContainsKey(p.Key)).OrderByDescending(p => p.Value.Length))
                if (path == old.Value || path.StartsWith(old.Value + ".", StringComparison.Ordinal)) return current[old.Key] + path[old.Value.Length..];
            return path;
        }
        var xml = XDocument.Parse(Editor.PrepareSave()); xml.Root!.Element("Page")!.SetAttributeValue("ComponentRoot", snapshot.ComponentRoot);
        foreach (var element in xml.Descendants("Elements").Elements()) if (element.Attribute("Component") is { } binding) binding.Value = Remap(binding.Value);
        string? selected = Editor.ViewModel.Selected?.Id;
        _loading = true;
        try
        {
            Editor.ViewModel.LoadConfiguration(snapshot, CdsLayout.Parse(xml.ToString()));
            if (selected is not null && Editor.ViewModel.Components.Any(n => n.Id == selected)) Editor.ViewModel.Select(selected);
            _schema = Schema(snapshot); _bindings = current;
        }
        finally { _loading = false; }
        OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(IsDirty)); Changed?.Invoke();
    }
    public void Validate()
    {
        RefreshConfiguration(); Editor.PrepareSave();
        var configured = CdsLayoutComposer.Compose(Read(), null).Nodes.Select(n => (n.Kind, n.Component)).ToHashSet();
        if (!Editor.ViewModel.Layout.Nodes.Where(n => n.Kind != "Terminal").Select(n => (n.Kind, n.Component)).ToHashSet().SetEquals(configured))
            throw new InvalidDataException(L10n.Get("cdsp.workspace.bindings"));
    }
    public FileWrite PrepareWrite(string machinePath)
    {
        Validate(); string path = LayoutPath(machinePath, _module!.Path);
        return new FileWrite(path, new UTF8Encoding(false).GetBytes(Editor.PrepareSave()), string.Equals(path, _path, StringComparison.OrdinalIgnoreCase) ? _fingerprint : null);
    }
    public FileWrite PrepareDiagramWrite(string machinePath)
    {
        string path = Path.ChangeExtension(LayoutPath(machinePath, _module!.Path), ".xaml");
        return new FileWrite(path, new UTF8Encoding(false).GetBytes(Editor.PrepareXamlExport()),
            string.Equals(path, DiagramPath, StringComparison.OrdinalIgnoreCase) ? _diagramFingerprint : null);
    }
    public void AcceptSave(FileWrite write, FileWrite diagramWrite)
    {
        _path = write.Path; _fingerprint = SHA256.HashData(write.Content); _savedXml = Editor.ViewModel.Layout.ToXml(); _isNew = false;
        _diagramFingerprint = SHA256.HashData(diagramWrite.Content);
        OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(IsDirty)); Changed?.Invoke();
    }
}
