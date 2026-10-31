using CommunityToolkit.Mvvm.Input;
using System.IO;
using xyz.Client.Common.Events;
using xyz.Client.Common.Rpc;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz._35021.Client.Manual.Models;
using xyz._35021.Client.Presentation.Chambers;

namespace xyz._35021.Client.Manual.ViewModels;

/// <summary>35021 腔体页复用平台手动命令；完整设备快照与外观布局由项目图分别使用。</summary>
public sealed class ChamberManualViewModel : xyz.Client.Manual.ViewModels.ChamberManualViewModel, IDisposable
{
    #region Column
    private ChamberLayoutModel? _layout;
    public ChamberLayoutModel? Layout { get => _layout; private set => SetProperty(ref _layout, value); }
    private string _diagramXaml = "";
    public string DiagramXaml { get => _diagramXaml; private set => SetProperty(ref _diagramXaml, value); }
    private ChamberDeviceDataDto? _snapshot;
    public ChamberDeviceDataDto? Snapshot { get => _snapshot; private set => SetProperty(ref _snapshot, value); }
    private string _layoutStatus = string.Empty;
    public string LayoutStatus { get => _layoutStatus; private set => SetProperty(ref _layoutStatus, value); }
    private IReadOnlyDictionary<string, ChamberDiagramState> _diagramStates = new Dictionary<string, ChamberDiagramState>();
    public IReadOnlyDictionary<string, ChamberDiagramState> DiagramStates { get => _diagramStates; private set => SetProperty(ref _diagramStates, value); }
    private bool _initialized;
    private bool _disposed;
    private int _layoutVersion;
    private string _savedXaml = "";
    private string _roster = "";
    #endregion

    #region Command
    public IAsyncRelayCommand ReloadLayoutCommand { get; }
    #endregion

    #region Service
    private readonly IChamberService _layoutService;
    #endregion

    public ChamberManualViewModel(string module, IChamberService? layoutService = null) : base(module)
    {
        _layoutService = layoutService ?? GrpcClientFactory.Create<IChamberService>();
        ReloadLayoutCommand = new AsyncRelayCommand(DoReloadLayout);
    }
    public override void Init()
    {
        if (_initialized) return;
        _initialized = true; base.Init();
        RemoteEventConnection.ConnectionChanged += OnConnectionChanged;
        _ = DoReloadLayout();
    }
    private void OnConnectionChanged(bool connected) { if (connected) _ = DoReloadLayout(); }
    public void UpdateDevices(ChamberDeviceDataDto snapshot)
    {
        OnDeviceDataReceived(snapshot);
    }
    protected override void OnDeviceDataReceived(ChamberDeviceDataDto snapshot)
    {
        if (_disposed || snapshot.Module != ModuleName) return;
        base.OnDeviceDataReceived(snapshot);
        Snapshot = snapshot;
        PublishDiagramStates(snapshot);
        string roster = string.Join("|", new[] { snapshot.Door?.Path, snapshot.Spin?.Path }.Concat(snapshot.Bowls.Select(b => b.Path))
            .Concat(snapshot.Arms.SelectMany(a => new[] { a.Path, a.Lift?.Path }.Concat(a.Nozzles.Select(n => n.Path)))));
        if (roster != _roster || DiagramXaml.Length == 0) { _roster = roster; RefreshDiagram(); }
    }
    private void PublishDiagramStates(ChamberDeviceDataDto snapshot)
    {
        string Key(string path) => ChamberLayoutComposer.Relative(path, Layout?.ComponentRoot ?? snapshot.Module, snapshot.Module);
        var states = new Dictionary<string, ChamberDiagramState>(StringComparer.OrdinalIgnoreCase);
        void Cylinder(ChamberCylinderDto? cylinder)
        {
            if (cylinder is not null) states[Key(cylinder.Path)] = new()
            { IsRaised = cylinder.Position == CylinderPosition.Opened, IsUnknown = cylinder.Position == CylinderPosition.Unknown };
        }
        Cylinder(snapshot.Door); foreach (var bowl in snapshot.Bowls) Cylinder(bowl);
        if (snapshot.Spin is { } spin) states[Key(spin.Path)] = new()
        {
            IsMoving = spin.HasPlcData && spin.IsBusy, IsError = spin.IsError || !spin.HasPlcData,
            RotationSpeed = spin.HasPlcData && spin.IsBusy ? Math.Abs(spin.CurrentSpeed) : 0, RotateClockwise = spin.CurrentSpeed >= 0
        };
        foreach (var arm in snapshot.Arms)
        {
            states[Key(arm.Path)] = new() { Angle = arm.CurrentPosition, IsMoving = arm.HasPlcData && arm.IsBusy, IsError = arm.IsError || !arm.HasPlcData };
            Cylinder(arm.Lift);
            foreach (var nozzle in arm.Nozzles) states[Key(nozzle.Path)] = new() { IsFlowing = nozzle.IsOn };
        }
        DiagramStates = states;
    }
    private void RefreshDiagram()
    {
        if (Snapshot is not { } snapshot) return;
        var composed = ChamberLayoutComposer.Compose(snapshot, Layout);
        bool matches = Layout is not null && composed.Parts.Select(p => (p.Kind, p.Id, p.ParentId)).ToHashSet()
            .SetEquals(Layout.Parts.Select(p => (p.Kind, p.Id, p.ParentId)));
        // 保存过的 XAML 原样使用；只有无文件或装机清单改变时才生成默认界面。
        DiagramXaml = _savedXaml.Length > 0 && matches ? _savedXaml : ChamberDiagramXaml.Export(composed);
    }
    private async Task DoReloadLayout()
    {
        int version = ++_layoutVersion;
        try
        {
            var response = await _layoutService.GetLayoutAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
            response.EnsureSuccess();
            var data = response.DeserializeData<ChamberLayoutDto>();
            if (_disposed || version != _layoutVersion) return;
            if (data.Module != ModuleName) throw new InvalidDataException("The layout belongs to another chamber.");
            var layout = data.Xaml.Length > 0 ? ChamberDiagramXaml.ReadLayout(data.Xaml) : data.Xml.Length == 0 ? null : ChamberLayoutModel.Parse(data.Xml);
            if (layout is not null && layout.ComponentRoot != data.ComponentRoot) throw new InvalidDataException("The layout root does not match.");
            _savedXaml = data.Xaml; Layout = layout;
            if (Snapshot is { } snapshot) PublishDiagramStates(snapshot);
            RefreshDiagram(); LayoutStatus = string.Empty;
        }
        catch (Exception failure)
        {
            if (_disposed || version != _layoutVersion) return;
            LayoutStatus = L10n.Get("m35021.chamber.layout_failed", failure.Message);
        }
    }
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _layoutVersion++;
        RemoteEventConnection.ConnectionChanged -= OnConnectionChanged; base.Dispose();
    }
}
