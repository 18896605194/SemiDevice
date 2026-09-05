using System.Windows;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.DataModels.Rpc;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Client.Manual.ViewModels;

/// <summary>
/// LoadPort 手动操作面板的 ViewModel：五个 gRPC 命令 + 状态绑定。
/// 按 ModuleName 实例化，每个 LoadPort 一个。
/// </summary>
public class LoadPortManualViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private readonly ILoadPortService _service;
    private string _moduleName;
    private int _state;
    private bool _isConnected;
    private bool _isPodPlaced;
    private bool _isBusy;
    private string _lastError = string.Empty;

    public LoadPortManualViewModel(string moduleName)
    {
        _moduleName = moduleName;
        _service = GrpcClientFactory.Create<ILoadPortService>();

        HomeCommand = new RelayCommand(() => ExecuteAction("Home"), () => !IsBusy);
        LoadCommand = new RelayCommand(() => ExecuteAction("Load"), () => !IsBusy);
        UnloadCommand = new RelayCommand(() => ExecuteAction("Unload"), () => !IsBusy);
        ResetCommand = new RelayCommand(() => ExecuteAction("Reset"), () => !IsBusy);
        AbortCommand = new RelayCommand(() => ExecuteAction("Abort"), () => !IsBusy);

        RefreshState();
    }

    #region 绑定属性

    public string ModuleName
    {
        get => _moduleName;
        private set => SetProperty(ref _moduleName, value);
    }

    public int State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public bool IsPodPlaced
    {
        get => _isPodPlaced;
        private set => SetProperty(ref _isPodPlaced, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// 最近一次失败的错误码句子（已按当前语言渲染）。
    /// </summary>
    public string LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    #endregion

    #region 命令

    public RelayCommand HomeCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand UnloadCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand AbortCommand { get; }

    #endregion

    private async void ExecuteAction(string action)
    {
        IsBusy = true;
        LastError = string.Empty;

        try
        {
            var response = action switch
            {
                "Home" => await _service.HomeAsync(ModuleName),
                "Load" => await _service.LoadAsync(ModuleName),
                "Unload" => await _service.UnloadAsync(ModuleName),
                "Reset" => await _service.ResetAsync(ModuleName),
                "Abort" => await _service.AbortAsync(ModuleName),
                _ => throw new NotSupportedException($"未知动作: {action}"),
            };

            if (!response.Success)
            {
                LastError = L10n.Get(response.Code, response.Args.ToArray());
            }
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshState();
        }
    }

    /// <summary>
    /// 轮询当前状态（后续替换为 EventBus 订阅）。
    /// </summary>
    public async void RefreshState()
    {
        try
        {
            var response = await _service.GetStateAsync(ModuleName);
            if (!response.Success)
            {
                return;
            }

            var dto = System.Text.Json.JsonSerializer.Deserialize<LoadPortDto>(response.Data);
            if (dto is null)
            {
                return;
            }

            State = dto.State;
            IsConnected = dto.IsConnected;
            IsPodPlaced = dto.IsPodPlaced;
        }
        catch
        {
            // 后端未启动时静默
        }
    }
}
