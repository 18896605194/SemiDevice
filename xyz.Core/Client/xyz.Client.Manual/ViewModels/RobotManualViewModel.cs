using System.Collections.ObjectModel;
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
/// 机械手手动操作面板 ViewModel：按钮发指令，状态靠订阅刷新；站点表 / 方位 / 平移 / 轴坐标都是后端推的，这里不写死。
/// Stop = 急停（AbortAsync，可顶替在途动作）、ResetDrive = 发设备复位清错（ResetAsync）。
/// </summary>
public class RobotManualViewModel : BaseViewModel, IDisposable
{
    #region Column

    /// <summary>
    /// 模块实例名，与 EventBus token / gRPC 参数一致，如 "Robot1"。
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// 显示模型：状态推送来就地刷新，手臂实例保留（动画不打断），所以是 get-only。
    /// </summary>
    public RobotModel Model { get; } = new();

    private int _selectedArm = 1;

    /// <summary>取放用的手指号（1 开始）。</summary>
    public int SelectedArm
    {
        get => _selectedArm;
        set => SetProperty(ref _selectedArm, value);
    }

    private string _selectedStation = string.Empty;

    /// <summary>取放站点名（站点表里的模块名，如 LoadPort1）；站点表还没推下来时为空。</summary>
    public string SelectedStation
    {
        get => _selectedStation;
        set => SetProperty(ref _selectedStation, value);
    }

    private int _selectedSlot = 1;

    /// <summary>取放槽位号。</summary>
    public int SelectedSlot
    {
        get => _selectedSlot;
        set => SetProperty(ref _selectedSlot, value);
    }

    /// <summary>手臂选择的数据源（1~ArmCount）。</summary>
    public ObservableCollection<int> ArmOptions { get; } = new();

    #endregion

    #region Command

    public IAsyncRelayCommand PickCommand { get; }

    public IAsyncRelayCommand PlaceCommand { get; }

    public IAsyncRelayCommand HomeCommand { get; }

    public IAsyncRelayCommand PowerOnCommand { get; }

    public IAsyncRelayCommand PowerOffCommand { get; }

    public IAsyncRelayCommand StopCommand { get; }

    public IAsyncRelayCommand ResetDriveCommand { get; }

    #endregion

    #region Service

    private readonly IRobotService _service;

    private IDisposable? _stateSubscription;

    #endregion

    public RobotManualViewModel(string moduleName)
    {
        ModuleName = moduleName;
        _service = GrpcClientFactory.Create<IRobotService>();

        PickCommand = new AsyncRelayCommand(DoPick);
        PlaceCommand = new AsyncRelayCommand(DoPlace);
        HomeCommand = new AsyncRelayCommand(DoHome);
        PowerOnCommand = new AsyncRelayCommand(DoPowerOn);
        PowerOffCommand = new AsyncRelayCommand(DoPowerOff);
        StopCommand = new AsyncRelayCommand(DoStop);
        ResetDriveCommand = new AsyncRelayCommand(DoResetDrive);
    }

    public override void Init()
    {
        // 先拉一次当前状态（含站点表），再订阅推送；只等推送的话页面刚打开会一直是空的。
        try
        {
            var response = _service.GetStateAsync(ModuleName).GetAwaiter().GetResult();
            response.EnsureSuccess();

            var json = response.Data?.TrimStart() ?? string.Empty;
            var robot = json.StartsWith('[')
                ? JsonHelper.Deserialize<List<RobotDto>>(json)?.FirstOrDefault()
                : JsonHelper.Deserialize<RobotDto>(json);
            if (robot is not null)
            {
                ApplyState(robot);
            }
        }
        catch (Exception exception)
        {
            ClientLog.Error(ModuleName, $"读取机械手状态失败：{exception.Message}");
        }

        _stateSubscription?.Dispose();
        _stateSubscription = EventBus.Register<RobotDto>(ModuleName, OnStateReceived);
    }

    public void Dispose()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
    }

    private void OnStateReceived(RobotDto dto)
    {
        ApplyState(dto);
    }

    private void ApplyState(RobotDto dto)
    {
        Model.Update(dto);

        // 手臂选择的数据源跟着手指数走；默认选 1 号。
        while (ArmOptions.Count > Model.ArmCount)
        {
            ArmOptions.RemoveAt(ArmOptions.Count - 1);
        }

        while (ArmOptions.Count < Model.ArmCount)
        {
            ArmOptions.Add(ArmOptions.Count + 1);
        }

        if (ArmOptions.Count > 0 && !ArmOptions.Contains(SelectedArm))
        {
            SelectedArm = ArmOptions[0];
        }

        // 站点下拉默认选站点表里的第一个。
        if (string.IsNullOrEmpty(SelectedStation) && Model.Stations.Count > 0)
        {
            SelectedStation = Model.Stations[0];
        }
    }

    private async Task DoPick()
    {
        var response = await _service.PickAsync(new RobotPickPlaceRequest
        {
            Module = ModuleName,
            Arm = SelectedArm,
            Station = SelectedStation,
            Slot = SelectedSlot,
        });
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Pick 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoPlace()
    {
        var response = await _service.PlaceAsync(new RobotPickPlaceRequest
        {
            Module = ModuleName,
            Arm = SelectedArm,
            Station = SelectedStation,
            Slot = SelectedSlot,
        });
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Place 失败：{L10n.Get(response.Code, response.Args)}");
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

    private async Task DoPowerOn()
    {
        var response = await _service.PowerOnAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"PowerOn 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoPowerOff()
    {
        var response = await _service.PowerOffAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"PowerOff 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoStop()
    {
        // Stop 按钮 = 急停：RPC 叫 AbortAsync，可顶替在途动作。
        var response = await _service.AbortAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"Stop 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }

    private async Task DoResetDrive()
    {
        // ResetDrive 按钮 = 发设备复位清错：RPC 叫 ResetAsync（组件基类的 Reset 清报警也在这条链上）。
        var response = await _service.ResetAsync(ModuleName);
        if (!response.Success)
        {
            ClientLog.Error(ModuleName, $"ResetDrive 失败：{L10n.Get(response.Code, response.Args)}");
        }
    }
}
