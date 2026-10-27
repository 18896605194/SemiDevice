using CommunityToolkit.Mvvm.Input;
using Mapster;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Manual.Models;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Manual.ViewModels;


public class LoadPortManualViewModel : BaseViewModel, IDisposable
{
    #region Column

    /// <summary>
    /// 如 "LoadPort1"
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// 留给界面展示的模型
    /// </summary>
    private LoadPortModel _model = new();

    public LoadPortModel Model
    {
        get => _model;
        private set => SetProperty(ref _model, value);
    }

    #endregion

    #region Command

    public IAsyncRelayCommand HomeCommand { get; }

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand UnloadCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IAsyncRelayCommand ProductionCommand { get; }

    public IAsyncRelayCommand MaintenanceCommand { get; }

    public IAsyncRelayCommand AutoCommand { get; }

    public IAsyncRelayCommand ManualCommand { get; }

    public IAsyncRelayCommand ReadCarrierIdCommand { get; }

    public IAsyncRelayCommand AbortCommand { get; }

    #endregion

    #region Service

    /// <summary>
    /// 后台Loadport 服务
    /// </summary>
    private readonly ILoadPortService _service;

    private IDisposable? _stateSubscription;

    #endregion

    public LoadPortManualViewModel(string moduleName)
    {
        ModuleName = moduleName;
        _service = GrpcClientFactory.Create<ILoadPortService>();

        HomeCommand = new AsyncRelayCommand(DoHome);
        LoadCommand = new AsyncRelayCommand(DoLoad);
        UnloadCommand = new AsyncRelayCommand(DoUnload);
        ResetCommand = new AsyncRelayCommand(DoReset);
        ProductionCommand = new AsyncRelayCommand(DoProduction);
        MaintenanceCommand = new AsyncRelayCommand(DoMaintenance);
        AutoCommand = new AsyncRelayCommand(DoAuto);
        ManualCommand = new AsyncRelayCommand(DoManual);
        ReadCarrierIdCommand = new AsyncRelayCommand(DoReadCarrierId);
        AbortCommand = new AsyncRelayCommand(DoAbort);
    }

    public override void Init()
    {
        // 先拉一次当前状态，再订阅推送；只等推送的话刚进页面会一直是空默认值。
        try
        {
            var response = _service.GetStateAsync(RpcRequest.Of((RpcParams.Module, ModuleName))).GetAwaiter().GetResult();
            response.EnsureSuccess();

            var json = response.Data?.TrimStart() ?? string.Empty;
            var port = json.StartsWith('[')
                ? JsonHelper.Deserialize<List<LoadPortDto>>(json)?.FirstOrDefault()
                : JsonHelper.Deserialize<LoadPortDto>(json);
            if (port is not null)
            {
                OnStateReceived(port);
            }
        }
        catch (Exception exception)
        {
            ClientLog.Error(ModuleName, $"读取 LoadPort 状态失败：{exception.Message}");
        }

        _stateSubscription?.Dispose();

        //订阅后台loadport数据
        _stateSubscription = RemoteEventBus.Register<LoadPortDto>(ModuleName, OnStateReceived);
    }



    private void OnStateReceived(LoadPortDto dto)
    {
        Model = dto.Adapt<LoadPortModel>();
    }

    private async Task DoHome()
    {
        var response = await _service.HomeAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Home 失败：{ErrorText.Of(response, ModuleStates.LoadPortText)}");
        }
    }

    private async Task DoLoad()
    {
        var response = await _service.LoadAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Load 失败：{ErrorText.Of(response, ModuleStates.LoadPortText)}");
        }
    }

    private async Task DoUnload()
    {
        var response = await _service.UnloadAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Unload 失败：{ErrorText.Of(response, ModuleStates.LoadPortText)}");
        }
    }

    private async Task DoReset()
    {
        var response = await _service.ResetAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Reset 失败：{ErrorText.Of(response, ModuleStates.LoadPortText)}");
        }
    }

    private async Task DoProduction()
    {
        var response = await _service.SetProductionAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"切生产模式失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoMaintenance()
    {
        var response = await _service.SetMaintenanceAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"切维护模式失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoAuto()
    {
        var response = await _service.AutoAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Auto 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoManual()
    {
        var response = await _service.ManualAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Manual 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoReadCarrierId()
    {
        var response = await _service.ReadCarrierIdAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Read ID 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoAbort()
    {
        var response = await _service.AbortAsync(RpcRequest.Of((RpcParams.Module, ModuleName)));
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Abort 失败：{ErrorText.Of(response, ModuleStates.LoadPortText)}");
        }
    }

    public void Dispose()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
    }
}
