using CommunityToolkit.Mvvm.Input;
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

/// <summary>
/// 腔体手动操作面板 ViewModel：按钮发指令，状态靠订阅刷新；启用、模式、片位、当前配方、部件状态都是后端推的，这里不写死。
/// Start 按配方框里的配方名起工艺（Process，只在空闲时允许）；Abort = 急停（AbortAsync，可顶替在途动作）、Reset = 清报警 + 设备复位清错（ResetAsync）。
/// 部件按钮（门、Bowl、旋转电机、摆臂、Lift、喷嘴）都走 PartActionCommand，命令参数是按钮自己的 ChamberPartActionItem。
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

    /// <summary>
    /// 部件显示模型（三维图和部件按钮绑它）：部件状态推送来就地刷新。
    /// </summary>
    public ChamberPartsModel Parts { get; } = new();

    private string _recipe = string.Empty;

    /// <summary>
    /// 要起的工艺配方名（Start 按它发 Process），配方库做好之前手填。输入框回车或离开时才提交，
    /// 所以 Start 不跟着它变灰（点 Start 时焦点离开输入框、先提交再执行），空着点 Start 在日志里提示。
    /// </summary>
    public string Recipe
    {
        get => _recipe;
        set => SetProperty(ref _recipe, value);
    }

    #endregion

    #region Command

    public IAsyncRelayCommand ProcessCommand { get; }

    public IAsyncRelayCommand HomeCommand { get; }

    public IAsyncRelayCommand AbortCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IAsyncRelayCommand OnlineCommand { get; }

    public IAsyncRelayCommand OfflineCommand { get; }

    /// <summary>部件手动动作：参数是按钮的 ChamberPartActionItem；一个动作在途时所有部件按钮都不可用。</summary>
    public IAsyncRelayCommand<ChamberPartActionItem> PartActionCommand { get; }

    #endregion

    #region Service

    private readonly IChamberService _service;

    private IDisposable? _stateSubscription;

    private IDisposable? _partsSubscription;

    #endregion

    public ChamberManualViewModel(string moduleName)
    {
        ModuleName = moduleName;
        _service = GrpcClientFactory.Create<IChamberService>();

        ProcessCommand = new AsyncRelayCommand(DoProcess);
        HomeCommand = new AsyncRelayCommand(DoHome);
        AbortCommand = new AsyncRelayCommand(DoAbort);
        ResetCommand = new AsyncRelayCommand(DoReset);
        OnlineCommand = new AsyncRelayCommand(DoOnline);
        OfflineCommand = new AsyncRelayCommand(DoOffline);
        PartActionCommand = new AsyncRelayCommand<ChamberPartActionItem>(DoPartAction);
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
            ClientLog.Error(ModuleName, L10n.Get("chambermanual.state_failed", exception.Message));
        }

        _stateSubscription?.Dispose();
        _stateSubscription = EventBus.Register<ChamberDto>(ModuleName, OnStateReceived);

        // 部件状态是留存消息：订上就补发最后一条，不用另外拉。
        _partsSubscription?.Dispose();
        _partsSubscription = EventBus.Register<ChamberPartsDto>(ModuleName, OnPartsReceived);
    }

    public void Dispose()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
        _partsSubscription?.Dispose();
        _partsSubscription = null;
    }

    private void OnStateReceived(ChamberDto dto)
    {
        Model.Update(dto);
    }

    private void OnPartsReceived(ChamberPartsDto dto)
    {
        Parts.Update(dto);
    }

    private async Task DoProcess()
    {
        if (string.IsNullOrWhiteSpace(Recipe))
        {
            ClientLog.Error(ModuleName, L10n.Get("chamber.recipe_required", ModuleName));
            return;
        }

        // 同步等工艺做完才回包（上限是腔体的 EC ProcessTimeout），期间按钮保持不可用；要停就按 Abort。
        var response = await _service.ProcessAsync(new ChamberProcessRequest
        {
            Module = ModuleName,
            Recipe = Recipe.Trim(),
        });
        LogFailure(response, "chambermanual.process");
    }

    private async Task DoHome()
    {
        LogFailure(await _service.HomeAsync(ModuleName), "action.home");
    }

    private async Task DoAbort()
    {
        // 急停：可顶替在途动作（被顶的调用方收到 module.action_aborted）。
        LogFailure(await _service.AbortAsync(ModuleName), "action.abort");
    }

    private async Task DoReset()
    {
        // 复位：组件基类先清报警，模块再发设备复位清错。
        LogFailure(await _service.ResetAsync(ModuleName), "action.reset");
    }

    private async Task DoOnline()
    {
        LogFailure(await _service.OnlineAsync(ModuleName), "action.online");
    }

    private async Task DoOffline()
    {
        LogFailure(await _service.OfflineAsync(ModuleName), "action.offline");
    }

    /// <summary>
    /// 部件手动动作：同步等部件做完才回包（上限是腔体的 EC PartActionTimeout），结果随部件状态推送刷到三维图上。
    /// </summary>
    private async Task DoPartAction(ChamberPartActionItem? item)
    {
        if (item is null)
        {
            return;
        }

        var response = await _service.PartActionAsync(new ChamberPartActionRequest
        {
            Module = ModuleName,
            Part = item.Path,
            Action = item.Action,
        });
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, L10n.Get("chambermanual.part_failed", item.Part, item.Text, ReasonOf(response)));
        }
    }

    /// <summary>动作失败时在顶栏日志里写"××失败：原因"；动作名取语言包。</summary>
    private void LogFailure(RpcResponse response, string actionKey)
    {
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, L10n.Get("chambermanual.action_failed", L10n.Get(actionKey), ReasonOf(response)));
        }
    }

    /// <summary>失败原因：有错误码按语言包翻，老接口没码才用 Message。</summary>
    private static string ReasonOf(RpcResponse response)
    {
        if (string.IsNullOrEmpty(response.Code))
        {
            return response.Message;
        }

        return L10n.Get(response.Code, response.Args);
    }
}
