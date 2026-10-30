using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ToolBuilder.Core;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace CdsAssemblyPreview;

public sealed partial class PreviewViewModel : BaseViewModel
{
    #region Column
    public ObservableCollection<CdsNode> Components { get; } = [];
    public Dictionary<string, bool?> ValveStates { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HardwareDemo> HardwareStates { get; } = new(StringComparer.Ordinal);
    public CdsSnapshot Snapshot { get; private set; } = new("", []);
    public CdsLayout Layout { get; private set; } = new();
    public string MachinePath => _machinePath;
    public int TankCount => Snapshot.Tanks.Count;
    public int ValveCount => ValveStates.Count;
    public int SensorCount => Snapshot.Sensors.Count() + Snapshot.Devices.Count(d => d.Kind == "Analog");
    public int PipeCount => Layout.Pipes.Count;
    public event Action? SceneChanged;
    public event Action? LayoutChanged;

    private CdsNode? _selected;
    public CdsNode? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            if (value is not null) SelectedPipeId = "";
            SelectionChanged();
        }
    }
    public bool IsTankSelected => Selected?.Kind == "Tank";
    public bool IsValveSelected => Selected?.Kind == "Valve";
    public string SelectionPath => Selected is not null ? Selected.Component.Length > 0 ? Snapshot.ComponentRoot + "." + Selected.Component : Selected.DisplayName
        : SelectedPipe is not null ? SelectedPipe.From + " → " + SelectedPipe.To : "";
    public int SelectedLevel
    {
        get => SelectedTank?.Data.Level ?? 0;
        set
        {
            if (SelectedTank is not { } tank || value == tank.Data.Level || value is < 0 or > 6) return;
            UpdateLevel(tank, value); OnPropertyChanged(); SceneChanged?.Invoke();
        }
    }
    public string ValveStateText => L10n.Get("cdsp.valve." + (ValveStates.GetValueOrDefault(SelectionPath) switch { true => "open", false => "closed", _ => "unknown" }));
    private bool _connectMode;
    public bool ConnectMode
    {
        get => _connectMode;
        set
        {
            if (value && IsSimulationMode) SelectedInspectorTab = 0;
            if (SetProperty(ref _connectMode, value)) { _startPort = null; SceneChanged?.Invoke(); }
        }
    }
    public string? StartPort => _startPort;
    private string _status = "";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    private string _layoutSource = "";
    public string LayoutSource { get => _layoutSource; private set => SetProperty(ref _layoutSource, value); }
    #endregion

    #region Command
    public IRelayCommand ExampleCommand { get; }
    public IRelayCommand ResetCommand { get; }
    public IRelayCommand ClearPipesCommand { get; }
    public IRelayCommand FillCommand { get; }
    public IRelayCommand SupplyCommand { get; }
    public IRelayCommand DrainCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand OpenValveCommand { get; }
    public IRelayCommand CloseValveCommand { get; }
    public IRelayCommand UnknownValveCommand { get; }
    public IRelayCommand AddInterfaceCommand { get; }
    public IRelayCommand DeleteSelectionCommand { get; }
    public IRelayCommand SelectModeCommand { get; }
    public IRelayCommand<Vector> NudgeCommand { get; }
    public IRelayCommand SensorOnCommand { get; }
    public IRelayCommand SensorOffCommand { get; }
    public IRelayCommand SensorUnknownCommand { get; }
    public IRelayCommand<HardwareState> HardwareStateCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand LoadCommand { get; }
    #endregion

    #region Service
    private readonly string _machinePath;
    private readonly string _moduleRoot;
    private bool _initialized;
    private string? _startPort;
    #endregion

    public PreviewViewModel(string? machinePath = null, string moduleRoot = "")
    {
        _machinePath = machinePath is null ? "" : Path.GetFullPath(machinePath); _moduleRoot = moduleRoot;
        ExampleCommand = new RelayCommand(DoExample); ResetCommand = new RelayCommand(DoReset);
        ClearPipesCommand = new RelayCommand(DoClearPipes);
        FillCommand = new RelayCommand(() => DoSimulate("fill")); SupplyCommand = new RelayCommand(() => DoSimulate("supply"));
        DrainCommand = new RelayCommand(() => DoSimulate("drain")); StopCommand = new RelayCommand(() => DoSimulate("idle"));
        OpenValveCommand = new RelayCommand(() => DoSetValve(true), () => IsValveSelected); CloseValveCommand = new RelayCommand(() => DoSetValve(false), () => IsValveSelected);
        UnknownValveCommand = new RelayCommand(() => DoSetValve(null), () => IsValveSelected); AddInterfaceCommand = new RelayCommand(DoAddInterface);
        DeleteSelectionCommand = new RelayCommand(DoDeleteSelection, () => CanDeleteSelection);
        SelectModeCommand = new RelayCommand(DoSelectMode);
        NudgeCommand = new RelayCommand<Vector>(DoNudge, _ => HasSelection);
        SensorOnCommand = new RelayCommand(() => DoSetSensor(true), () => IsSensorSelected);
        SensorOffCommand = new RelayCommand(() => DoSetSensor(false), () => IsSensorSelected);
        SensorUnknownCommand = new RelayCommand(() => DoSetSensor(null), () => IsSensorSelected);
        HardwareStateCommand = new RelayCommand<HardwareState>(DoHardwareState);
        SaveCommand = new AsyncRelayCommand(DoSave); LoadCommand = new AsyncRelayCommand(DoLoad);
    }

    public override void Init()
    {
        if (_initialized) return;
        _initialized = true;
        if (_machinePath.Length == 0) return;
        // 只有工具读取 machine.xml；不构造后端元件，不调用设备服务。
        var document = ConfigDocument.Open(_machinePath);
        var module = document.Settings.Single(e => ConfigDocument.PathOf(e) == _moduleRoot);
        bool Type(System.Xml.Linq.XElement e, string name) => ConfigDocument.Attribute(e, "Type").Split(',')[0].Trim().EndsWith("." + name, StringComparison.Ordinal);
        var tanks = module.Descendants("Setting").Where(e => Type(e, "TankElement")).Select(e =>
        {
            DiSensorDto? Signal(string name) => e.Elements("Setting").Any(n => ConfigDocument.Attribute(n, "Name") == name)
                ? new DiSensorDto { Name = name, IsActive = null } : null;
            var groups = e.Descendants("Setting").Where(g => Type(g, "ValveGroupElement")).Select(g =>
                new CdsRouteDefinition(ConfigDocument.Attribute(g, "Name").ToLowerInvariant(), g.Descendants("Setting")
                    .Where(v => Type(v, "ValveElement")).Select(ConfigDocument.PathOf).ToArray())).ToArray();
            return new CdsTankSnapshot(new TankDto { Path = ConfigDocument.PathOf(e), LL = Signal("LL"), L = Signal("L"),
                H = Signal("H"), HH = Signal("HH"), Overfill = Signal("Overfill") }, groups);
        }).ToArray();
        Snapshot = new CdsSnapshot(_moduleRoot, tanks);
        foreach (var path in tanks.SelectMany(t => t.Routes).SelectMany(g => g.ValvePaths)) ValveStates.Add(path, null);
        DoReset();
    }

    public void LoadConfiguration(CdsSnapshot snapshot, CdsLayout? saved)
    {
        var composed = CdsLayoutComposer.Compose(snapshot, saved);
        Snapshot = snapshot;
        var valves = snapshot.ValvePaths.ToHashSet(StringComparer.Ordinal);
        foreach (string path in ValveStates.Keys.Except(valves).ToArray()) ValveStates.Remove(path);
        foreach (string path in valves) ValveStates.TryAdd(path, null);
        var devices = snapshot.Devices.Where(d => d.Kind is "Hardware" or "Analog").ToArray();
        foreach (string path in HardwareStates.Keys.Except(devices.Select(d => d.Path)).ToArray()) HardwareStates.Remove(path);
        foreach (var device in devices)
        {
            var definition = device.Kind == "Analog" ? new HardwareDefinition(FluidHardwareKind.FlowMeter, "measurement", HardwareRole.Monitor, "AI", "", 0, -1e12, 1e12)
                : HardwareCatalog.Create().Single(d => d.Kind == device.VisualKind);
            if (HardwareStates.TryGetValue(device.Path, out var existing) && existing.Definition.Kind == definition.Kind) continue;
            var demo = new HardwareDemo(definition) { Reading = null, State = HardwareState.Unknown };
            if (device.Kind == "Analog") demo.Unit = "";
            demo.PropertyChanged += (_, _) => SceneChanged?.Invoke(); HardwareStates[device.Path] = demo;
        }
        foreach (string name in new[] { nameof(Snapshot), nameof(TankCount), nameof(ValveCount), nameof(SensorCount) }) OnPropertyChanged(name);
        ApplyLayout(composed);
    }
    private void DoHardwareState(HardwareState state)
    {
        if (SelectedHardware is not { } device) return;
        if (state == HardwareState.Unknown) device.Reading = null;
        else if (device.Reading is null) device.Reading = device.Definition.DefaultValue;
        device.State = state;
    }
    public string PrepareSave()
    {
        if (Parameters.OfType<CdsTextParameter>().Any(p => p.Error.Length > 0)) throw new InvalidDataException(L10n.Get("cdsp.parameters.invalid"));
        string xml = Layout.ToXml(); CdsLayout.Parse(xml); return xml;
    }

    private CdsTankSnapshot? SelectedTank => Snapshot.Tanks.SingleOrDefault(t => t.Data.Path == SelectionPath);
    public void DoExample()
    {
        try
        {
            ApplyLayout(CdsLayout.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Example.xml"))));
            LayoutSource = L10n.Get("cdsp.layout.example"); Status = L10n.Get("cdsp.example.note");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Xml.XmlException)
        { Status = L10n.Get("cdsp.failed", ex.Message); }
    }
    public void DoReset()
    {
        ApplyLayout(null); LayoutSource = L10n.Get("cdsp.layout.empty"); Status = L10n.Get("cdsp.empty.note");
    }
    private void DoClearPipes() { SelectedPipeId = ""; Layout.Pipes.Clear(); PipeItems.Clear(); _startPort = null; Changed(); Status = L10n.Get("cdsp.empty.note"); }
    public void ApplyLayout(CdsLayout? saved)
    {
        var layout = CdsLayoutComposer.Compose(Snapshot, saved); // 成功验证后再替换。
        Selected = null; SelectedPipeId = "";
        Layout = layout; Components.Clear(); PipeItems.Clear();
        VerticalGuideX = null;
        foreach (var node in layout.Nodes) Components.Add(node);
        foreach (var pipe in layout.Pipes) PipeItems.Add(pipe);
        Selected = Components.FirstOrDefault(); _startPort = null; Changed();
    }
    public void Select(string id) => Selected = Components.SingleOrDefault(n => n.Id == id);
    public void Move(string id, Point position)
    {
        int index = Layout.Nodes.FindIndex(n => n.Id == id);
        var node = Layout.Nodes[index];
        var previousGuide = VerticalGuideX;
        SetNodePosition(index, SnapNodePosition(node, position));
        if (Layout.Nodes[index] == node && previousGuide != VerticalGuideX) SceneChanged?.Invoke();
    }
    public void Connect(string port)
    {
        if (!ConnectMode) return;
        if (_startPort is null) { _startPort = port; Status = L10n.Get("cdsp.connect.end"); SceneChanged?.Invoke(); return; }
        if (_startPort == port) { _startPort = null; SceneChanged?.Invoke(); return; }
        var start = Layout.Port(_startPort); var end = Layout.Port(port);
        var bends = DefaultBends(start, end);
        string id = "Manual" + Guid.NewGuid().ToString("N")[..8];
        Layout.Pipes.Add(new CdsPipe(id, _startPort, port, "manual", "", bends,
            WallColor: Color.FromRgb(130, 145, 160), FlowColor: Color.FromRgb(96, 188, 222)));
        PipeItems.Add(Layout.Pipes[^1]);
        _startPort = null; LayoutSource = L10n.Get("cdsp.layout.edited"); Changed(); Status = L10n.Get("cdsp.connect.start");
    }
    private void DoAddInterface()
    {
        string id = "Interface" + Guid.NewGuid().ToString("N")[..5];
        var node = new CdsNode("Terminal", id, "", L10n.Get("cdsp.interface"), 60, 60, 12, 12);
        Layout.Nodes.Add(node); Components.Add(node); SelectedInspectorTab = 0; Select(node.Id); Changed();
    }
    public void DoSimulate(string kind)
    {
        if (SelectedTank is not { } tank) return;
        foreach (var group in tank.Routes) foreach (string valve in group.ValvePaths) ValveStates[valve] = group.Kind == kind;
        tank.Data.State = kind switch { "fill" => 2, "drain" => 3, "supply" => 5, _ => 0 };
        if (tank.Data.Level == 0 && kind != "idle") UpdateLevel(tank, 3);
        OnPropertyChanged(nameof(SelectedLevel)); SceneChanged?.Invoke();
    }
    private static void UpdateLevel(CdsTankSnapshot tank, int level)
    {
        tank.Data.Level = level;
        foreach (var (sensor, threshold) in new[] { (tank.Data.LL, 2), (tank.Data.L, 3), (tank.Data.H, 4), (tank.Data.HH, 5), (tank.Data.Overfill, 6) })
            if (sensor is not null) sensor.IsActive = level == 0 ? null : level >= threshold;
    }
    private void DoSetValve(bool? state)
    {
        if (!IsValveSelected) return;
        ValveStates[SelectionPath] = state; OnPropertyChanged(nameof(ValveStateText));
        Status = L10n.Get("cdsp.valve.changed", SelectionPath, ValveStateText); SceneChanged?.Invoke();
    }
    private async Task DoSave()
    {
        var dialog = new SaveFileDialog { Filter = "CDS layout (*.xml)|*.xml", FileName = "CDS.IPA.preview.xml" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (Parameters.OfType<CdsTextParameter>().Any(p => p.Error.Length > 0)) throw new IOException(L10n.Get("cdsp.parameters.invalid"));
            if (Path.GetFullPath(dialog.FileName).Equals(_machinePath, StringComparison.OrdinalIgnoreCase)) throw new IOException(L10n.Get("cdsp.protect.machine"));
            string xml = Layout.ToXml(); CdsLayout.Parse(xml);
            await File.WriteAllTextAsync(dialog.FileName, xml); Status = L10n.Get("cdsp.saved", dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { Status = L10n.Get("cdsp.failed", ex.Message); }
    }
    private async Task DoLoad()
    {
        var dialog = new OpenFileDialog { Filter = "CDS layout (*.xml)|*.xml" };
        if (dialog.ShowDialog() != true) return;
        try { ApplyLayout(CdsLayout.Parse(await File.ReadAllTextAsync(dialog.FileName))); LayoutSource = Path.GetFileName(dialog.FileName); Status = L10n.Get("cdsp.loaded"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Xml.XmlException)
        { Status = L10n.Get("cdsp.failed", ex.Message); }
    }
    private void Changed() { OnPropertyChanged(nameof(PipeCount)); SceneChanged?.Invoke(); LayoutChanged?.Invoke(); }
}
