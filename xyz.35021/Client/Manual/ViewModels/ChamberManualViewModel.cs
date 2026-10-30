using CommunityToolkit.Mvvm.Input;
using System.IO;
using xyz.Client.Common.Events;
using xyz.Client.Common.Rpc;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

namespace xyz._35021.Client.Manual.ViewModels;

/// <summary>35021 腔体页复用平台手动命令；完整设备快照与外观布局由项目图分别使用。</summary>
public sealed class ChamberManualViewModel : xyz.Client.Manual.ViewModels.ChamberManualViewModel, IDisposable
{
    #region Column
    private ChamberLayoutModel? _layout;
    public ChamberLayoutModel? Layout { get => _layout; private set => SetProperty(ref _layout, value); }
    private ChamberDeviceDataDto? _snapshot;
    public ChamberDeviceDataDto? Snapshot { get => _snapshot; private set => SetProperty(ref _snapshot, value); }
    private string _layoutStatus = string.Empty;
    public string LayoutStatus { get => _layoutStatus; private set => SetProperty(ref _layoutStatus, value); }
    public ChamberVisualCatalog VisualCatalog { get; }
    #endregion

    #region Command
    public IAsyncRelayCommand ReloadLayoutCommand { get; }
    #endregion

    #region Service
    private readonly IChamberService _layoutService;
    private bool _initialized;
    private bool _disposed;
    private int _layoutVersion;
    #endregion

    public ChamberManualViewModel(string module, ChamberVisualCatalog visuals, IChamberService? layoutService = null) : base(module)
    {
        VisualCatalog = visuals;
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
            var layout = data.Xml.Length == 0 ? null : ChamberLayoutModel.Parse(data.Xml);
            if (layout is not null && layout.ComponentRoot != data.ComponentRoot) throw new InvalidDataException("The layout root does not match.");
            Layout = layout; LayoutStatus = string.Empty;
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
