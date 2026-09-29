using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Manual.Models;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Manual.ViewModels;

/// <summary>
/// 腔体手动操作面板 ViewModel：按钮发指令，状态靠订阅刷新；启用、模式、片位、当前配方都是后端推的，这里不写死。
/// Process 按填的配方名起工艺（只在空闲时允许）；Abort = 急停（AbortAsync，可顶替在途动作）、Reset = 清报警 + 设备复位清错（ResetAsync）。
/// </summary>
public class ChamberManualViewModel : BaseViewModel, IDisposable
{
    #region Column

    /// <summary>
    /// 模块实例名，与 EventBus token / gRPC 参数一致，如 "Chamber1"。
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// 显示模型：状态推送来就地刷新，所以是 get-only。
    /// </summary>
    public ChamberModel Model { get; } = new();

    private string _recipe = string.Empty;

    /// <summary>要起的工艺配方名（Process 按它发）；空着 Process 按钮不可用。</summary>
    public string Recipe
    {
        get => _recipe;
        set
        {
            if (SetProperty(ref _recipe, value))
            {
                ProcessCommand.NotifyCanExecuteChanged();
            }
        }
    }

    #endregion

    #region Command

    public IAsyncRelayCommand ProcessCommand { get; }

    public IAsyncRelayCommand HomeCommand { get; }

    public IAsyncRelayCommand AbortCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IAsyncRelayCommand OnlineCommand { get; }

    public IAsyncRelayCommand OfflineCommand { get; }

    #endregion

    #region Service

    private readonly IChamberService _service;

    private IDisposable? _stateSubscription;

    #endregion

    public ChamberManualViewModel(string moduleName)
    {
        ModuleName = moduleName;
        _service = GrpcClientFactory.Create<IChamberService>();

        ProcessCommand = new AsyncRelayCommand(DoProcess, CanProcess);
        HomeCommand = new AsyncRelayCommand(DoHome);
        AbortCommand = new AsyncRelayCommand(DoAbort);
        ResetCommand = new AsyncRelayCommand(DoReset);
        OnlineCommand = new AsyncRelayCommand(DoOnline);
        OfflineCommand = new AsyncRelayCommand(DoOffline);
    }

    public override void Init()
    {
        // 先拉一次当前状态，再订阅推送；只等推送的话页面刚打开会一直是空的。
        try
        {
            var response = _service.GetStateAsync(ModuleName).GetAwaiter().GetResult();
            response.EnsureSuccess();

            var json = response.Data?.TrimStart() ?? string.Empty;
            var chamber = json.StartsWith('[')
                ? JsonHelper.Deserialize<List<ChamberDto>>(json)?.FirstOrDefault()
                : JsonHelper.Deserialize<ChamberDto>(json);
            if (chamber is not null)
            {
                OnStateReceived(chamber);
            }
        }
        catch (Exception exception)
        {
            ClientLog.Error(ModuleName, $"读取腔体状态失败：{exception.Message}");
        }

        _stateSubscription?.Dispose();
        _stateSubscription = EventBus.Register<ChamberDto>(ModuleName, OnStateReceived);
    }

    public void Dispose()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
    }

    private void OnStateReceived(ChamberDto dto)
    {
        Model.Update(dto);
    }

    private bool CanProcess()
    {
        return !string.IsNullOrWhiteSpace(Recipe);
    }

    private async Task DoProcess()
    {
        // 同步等工艺做完才回包（上限是腔体的 EC ProcessTimeout），期间按钮保持不可用；要停就按 Abort。
        var response = await _service.ProcessAsync(new ChamberProcessRequest
        {
            Module = ModuleName,
            Recipe = Recipe.Trim(),
        });
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Process 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoHome()
    {
        var response = await _service.HomeAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Home 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoAbort()
    {
        // 急停：可顶替在途动作（被顶的调用方收到 module.action_aborted）。
        var response = await _service.AbortAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Abort 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoReset()
    {
        // 复位：组件基类先清报警，模块再发设备复位清错。
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
}
