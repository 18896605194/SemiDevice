using CommunityToolkit.Mvvm.Input;
using Mapster;
using xyz.Client.DataModels.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Manual.Models;
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

    private Task DoHome()
    {
        return _service.HomeAsync(ModuleName);
    }

    private Task DoLoad()
    {
        return _service.LoadAsync(ModuleName);
    }

    private Task DoUnload()
    {
        return _service.UnloadAsync(ModuleName);
    }

    private Task DoReset()
    {
        return _service.ResetAsync(ModuleName);
    }

    private Task DoOnline()
    {
        return _service.OnlineAsync(ModuleName);
    }

    private Task DoOffline()
    {
        return _service.OfflineAsync(ModuleName);
    }

    private Task DoAbort()
    {
        return _service.AbortAsync(ModuleName);
    }
}
