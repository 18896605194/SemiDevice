using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using xyz.Client.DataModels.ViewModels;

namespace CdsAssemblyPreview;

/// <summary>仅用于组合预览的本地示例，不属于装机清单或可保存的CDS布局。</summary>
public sealed class CdsPipelinePreviewViewModel : BaseViewModel
{
    public const double CanvasWidth = 1200, CanvasHeight = 540;
    #region Column
    public ObservableCollection<CdsNode> Parts { get; } = [];
    public HardwareDemo Pump { get; private set; } = null!;
    public HardwareDemo Heater { get; private set; } = null!;
    public event Action? SceneChanged;
    private CdsNode? _selected;
    public CdsNode? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            foreach (string key in new[] { nameof(X), nameof(Y), nameof(SymbolWidth), nameof(SymbolHeight), nameof(Angle), nameof(MaxX), nameof(MaxY),
                nameof(SelectedHardware), nameof(IsProcessSelected), nameof(IsValveSelected), nameof(IsTankSelected), nameof(SelectedValve), nameof(SelectedTitle) }) OnPropertyChanged(key);
            SceneChanged?.Invoke();
        }
    }
    public string SelectedTitle => Selected?.Text ?? "";
    public HardwareDemo? SelectedHardware => Selected?.Kind switch { "Pump" => Pump, "Heater" => Heater, _ => null };
    public bool IsProcessSelected => SelectedHardware is not null;
    public bool IsValveSelected => Selected?.Kind == "Valve";
    public bool IsTankSelected => Selected?.Kind == "Tank";
    public double X { get => Selected?.X ?? 0; set { if (Selected is { } n) Move(n.Id, new Point(value, n.Y)); } }
    public double Y { get => Selected?.Y ?? 0; set { if (Selected is { } n) Move(n.Id, new Point(n.X, value)); } }
    public double MaxX => CanvasWidth - (Selected?.Width ?? 0);
    public double MaxY => CanvasHeight - (Selected?.Height ?? 0);
    public double SymbolWidth { get => Selected?.Width ?? 0; set { if (Selected is { } n && double.IsFinite(value) && value is >= 12 and <= 400) Replace(n with { Width = Math.Min(value, CanvasWidth - n.X) }); } }
    public double SymbolHeight { get => Selected?.Height ?? 0; set { if (Selected is { } n && double.IsFinite(value) && value is >= 12 and <= 400) Replace(n with { Height = Math.Min(value, CanvasHeight - n.Y) }); } }
    public double Angle { get => Selected?.Angle ?? 0; set { if (Selected is { } n && double.IsFinite(value) && value is >= -180 and <= 180) Replace(n with { Angle = value }); } }
    private bool? _inletValve = true;
    public bool? InletValve { get => _inletValve; set { if (SetProperty(ref _inletValve, value)) NotifyScene(); } }
    private bool? _outletValve = true;
    public bool? OutletValve { get => _outletValve; set { if (SetProperty(ref _outletValve, value)) NotifyScene(); } }
    public bool? SelectedValve => Selected?.Id == "V1" ? InletValve : OutletValve;
    private double _fillPercent = 60;
    public double FillPercent { get => _fillPercent; set { if (double.IsFinite(value) && value is >= 0 and <= 100 && SetProperty(ref _fillPercent, value)) NotifyScene(); } }
    private bool _showInternals = true;
    public bool ShowInternals { get => _showInternals; set { if (SetProperty(ref _showInternals, value)) NotifyScene(); } }
    private double _powerPercent = 65;
    public double PowerPercent { get => _powerPercent; set { if (double.IsFinite(value) && value is >= 0 and <= 100 && SetProperty(ref _powerPercent, value)) { OnPropertyChanged(nameof(PowerFraction)); NotifyScene(); } } }
    public double PowerFraction => PowerPercent / 100;
    private double _pipeDiameter = 12;
    public double PipeDiameter { get => _pipeDiameter; set { if (double.IsFinite(value) && value is >= 4 and <= 28 && SetProperty(ref _pipeDiameter, value)) NotifyScene(); } }
    public bool IsFlowing => Pump.State == HardwareState.Active && Pump.Reading > 0 && InletValve == true && OutletValve == true;
    #endregion
    #region Command
    public IRelayCommand RunCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand ResetCommand { get; }
    public IRelayCommand<HardwareState> StateCommand { get; }
    public IRelayCommand<string> ValveCommand { get; }
    public IRelayCommand<Vector> NudgeCommand { get; }
    #endregion
    #region Service
    private bool _initialized;
    #endregion
    public CdsPipelinePreviewViewModel()
    {
        RunCommand = new RelayCommand(DoRun); StopCommand = new RelayCommand(DoStop); ResetCommand = new RelayCommand(DoReset);
        StateCommand = new RelayCommand<HardwareState>(DoState); ValveCommand = new RelayCommand<string>(DoValve);
        NudgeCommand = new RelayCommand<Vector>(DoNudge);
    }
    public override void Init()
    {
        if (_initialized) return;
        var catalog = HardwareCatalog.Create();
        Pump = new HardwareDemo(catalog.Single(d => d.Kind == FluidHardwareKind.CentrifugalPump));
        Heater = new HardwareDemo(catalog.Single(d => d.Kind == FluidHardwareKind.InlineHeater));
        Pump.PropertyChanged += (_, _) => NotifyScene(); Heater.PropertyChanged += (_, _) => NotifyScene();
        _initialized = true; DoReset();
    }
    public CdsNode Part(string id) => Parts.Single(n => n.Id == id);
    public void Select(string id) => Selected = Part(id);
    public void Move(string id, Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        var n = Part(id); Replace(n with { X = Math.Clamp(point.X, 0, CanvasWidth - n.Width), Y = Math.Clamp(point.Y, 0, CanvasHeight - n.Height) });
    }
    private void Replace(CdsNode node)
    {
        int index = Parts.IndexOf(Part(node.Id)); if (Parts[index] == node) return;
        bool wasSelected = Selected?.Id == node.Id;
        Parts[index] = node;
        if (wasSelected) Selected = node;
        else SceneChanged?.Invoke();
    }
    private void DoNudge(Vector delta) { if (Selected is { } n) Move(n.Id, new Point(n.X + delta.X, n.Y + delta.Y)); }
    private void DoReset()
    {
        Parts.Clear();
        Parts.Add(new CdsNode("Tank", "TK1", "", "Tank1", 75, 225, 160, 230));
        double y = Port("TK1", true).Y;
        var pumpBounds = ProcessHardwareProjection.Bounds(FluidHardwareKind.CentrifugalPump);
        double pumpWidth = 250, pumpHeight = pumpWidth / pumpBounds.Width * pumpBounds.Height;
        double pumpY = y - ProcessHardwareProjection.Port(FluidHardwareKind.CentrifugalPump, false).Y * pumpHeight;
        Parts.Add(new CdsNode("Valve", "V1", "", "V1", 310, y - 18, 36, 36));
        Parts.Add(new CdsNode("Pump", "P1", "", "P1", 420, pumpY, pumpWidth, pumpHeight));
        double heaterY = pumpY - 65;
        var heaterBounds = ProcessHardwareProjection.Bounds(FluidHardwareKind.InlineHeater);
        double heaterWidth = 275, heaterHeight = heaterWidth / heaterBounds.Width * heaterBounds.Height;
        Parts.Add(new CdsNode("Heater", "H1", "", "H1", 795,
            heaterY - ProcessHardwareProjection.Port(FluidHardwareKind.InlineHeater, false).Y * heaterHeight, heaterWidth, heaterHeight));
        Parts.Add(new CdsNode("Valve", "V2", "", "V2", 1100, heaterY - 18, 36, 36));
        Parts.Add(new CdsNode("Terminal", "OUT", "", "OUT", 1170, heaterY - 6, 12, 12));
        InletValve = true; OutletValve = true; FillPercent = 60; PowerPercent = 65; PipeDiameter = 12; ShowInternals = true;
        Pump.State = HardwareState.Stopped; Pump.Reading = 1450; Pump.Target = 1450;
        Heater.State = HardwareState.Stopped; Heater.Reading = 65; Heater.Target = 75;
        Selected = Part("P1"); NotifyScene();
    }
    private void DoRun()
    {
        InletValve = true; OutletValve = true;
        SetState(Pump, HardwareState.Active); SetState(Heater, HardwareState.Active);
    }
    private void DoStop() { SetState(Pump, HardwareState.Stopped); SetState(Heater, HardwareState.Stopped); }
    private void DoState(HardwareState state) { if (SelectedHardware is { } hardware) SetState(hardware, state); }
    private static void SetState(HardwareDemo hardware, HardwareState state)
    {
        if (state == HardwareState.Unknown) hardware.Reading = null;
        else if (hardware.Reading is null) hardware.Reading = hardware.Definition.DefaultValue;
        hardware.State = state;
    }
    private void DoValve(string? state)
    {
        if (!IsValveSelected) return;
        bool? open = state switch { "open" => true, "close" => false, _ => null };
        if (Selected?.Id == "V1") InletValve = open; else OutletValve = open;
        OnPropertyChanged(nameof(SelectedValve));
    }
    private void NotifyScene() { OnPropertyChanged(nameof(IsFlowing)); SceneChanged?.Invoke(); }

    public Point Port(string id, bool outlet)
    {
        var n = Part(id);
        if (n.Kind == "Tank")
        {
            var p = n.Transform().Transform(outlet ? new System.Windows.Media.Media3D.Point3D(1, 1.065, 0) : new System.Windows.Media.Media3D.Point3D(-1, 2.13, 0));
            return new Point(p.X, -p.Y);
        }
        var port = n.Kind switch
        {
            "Pump" => ProcessHardwareProjection.Port(FluidHardwareKind.CentrifugalPump, outlet),
            "Heater" => ProcessHardwareProjection.Port(FluidHardwareKind.InlineHeater, outlet),
            "Terminal" => new Point(0.5, 0.5),
            _ => new Point(outlet ? 1 : 0, 0.5)
        };
        return n.RotatePoint(new Point(n.X + port.X * n.Width, n.Y + port.Y * n.Height));
    }
    public IReadOnlyList<Point[]> Routes()
    {
        Point[] Route(Point a, Point b, bool rise = false) => Math.Abs(a.X - b.X) < 0.01 || Math.Abs(a.Y - b.Y) < 0.01 ? [a, b]
            : rise ? [a, new Point(a.X, b.Y), b] : [a, new Point((a.X + b.X) / 2, a.Y), new Point((a.X + b.X) / 2, b.Y), b];
        return [Route(Port("TK1", true), Port("V1", false)), Route(Port("V1", true), Port("P1", false)),
            Route(Port("P1", true), Port("H1", false), true), Route(Port("H1", true), Port("V2", false)), Route(Port("V2", true), Port("OUT", false))];
    }
}
