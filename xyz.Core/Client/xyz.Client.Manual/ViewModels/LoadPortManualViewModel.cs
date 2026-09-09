using CommunityToolkit.Mvvm.Input;
using Mapster;
using xyz.Client.DataModels.Log;
using xyz.Client.DataModels.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Manual.Models;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Manual.ViewModels;


public class LoadPortManualViewModel : BaseViewModel, IDisposable
{
    #region Column

    /// <summary>
    /// 模块实例名，与 EventBus token / gRPC 参数一致，如 "LoadPort1"。
    /// </summary>
    public string ModuleName { get; }

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

    public IAsyncRelayCommand OnlineCommand { get; }

    public IAsyncRelayCommand OfflineCommand { get; }

    public IAsyncRelayCommand AbortCommand { get; }

    #endregion

    #region Service

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
        OnlineCommand = new AsyncRelayCommand(DoOnline);
        OfflineCommand = new AsyncRelayCommand(DoOffline);
        AbortCommand = new AsyncRelayCommand(DoAbort);
    }

    public override void Init()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = EventBus.Register<LoadPortDto>(ModuleName, OnStateReceived);
    }

    public void Dispose()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
    }

    private void OnStateReceived(LoadPortDto dto)
    {
        Model = dto.Adapt<LoadPortModel>();
    }

    private async Task DoHome()
    {
        var response = await _service.HomeAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Home 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoLoad()
    {
        var response = await _service.LoadAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Load 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoUnload()
    {
        var response = await _service.UnloadAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Unload 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoReset()
    {
        var response = await _service.ResetAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Reset 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoOnline()
    {
        var response = await _service.OnlineAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Online 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoOffline()
    {
        var response = await _service.OfflineAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Offline 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoAbort()
    {
        var response = await _service.AbortAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Abort 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }
}
