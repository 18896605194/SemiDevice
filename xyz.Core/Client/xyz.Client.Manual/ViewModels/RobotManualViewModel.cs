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
/// 机械手手动操作面板 ViewModel：按钮发指令，状态靠订阅刷新；站点表（含各站点槽数）/ 方位 / 平移 / 轴坐标都是后端推的，这里不写死。
/// Pick 从源站点槽位取片、Place 往目标站点槽位放片；Abort = 急停（AbortAsync，可顶替在途动作）、Reset = 清报警 + 设备复位清错（ResetAsync）。
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

    private string _sourceStation = string.Empty;

    /// <summary>源站点（Pick 从这儿取片），站点表里的模块名如 LoadPort1；换站点时源槽位下拉跟着换。</summary>
    public string SourceStation
    {
        get => _sourceStation;
        set
        {
            if (SetProperty(ref _sourceStation, value))
            {
                UpdateSourceSlots();
            }
        }
    }

    private int _sourceSlot = 1;

    /// <summary>源槽位号。</summary>
    public int SourceSlot
    {
        get => _sourceSlot;
        set => SetProperty(ref _sourceSlot, value);
    }

    private string _targetStation = string.Empty;

    /// <summary>目标站点（Place 往这儿放片）；换站点时目标槽位下拉跟着换。</summary>
    public string TargetStation
    {
        get => _targetStation;
        set
        {
            if (SetProperty(ref _targetStation, value))
            {
                UpdateTargetSlots();
            }
        }
    }

    private int _targetSlot = 1;

    /// <summary>目标槽位号。</summary>
    public int TargetSlot
    {
        get => _targetSlot;
        set => SetProperty(ref _targetSlot, value);
    }

    /// <summary>手臂选择的数据源（1~ArmCount）。</summary>
    public ObservableCollection<int> ArmOptions { get; } = new();

    /// <summary>源槽位下拉：1~源站点的槽数（站点模块在 sc.xml 里配的 SlotCount，LoadPort 25、腔体 1）。</summary>
    public ObservableCollection<int> SourceSlotOptions { get; } = new();

    /// <summary>目标槽位下拉：1~目标站点的槽数。</summary>
    public ObservableCollection<int> TargetSlotOptions { get; } = new();

    #endregion

    #region Command

    public IAsyncRelayCommand PickCommand { get; }

    public IAsyncRelayCommand PlaceCommand { get; }

    public IAsyncRelayCommand HomeCommand { get; }

    public IAsyncRelayCommand AbortCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IAsyncRelayCommand PowerOnCommand { get; }

    public IAsyncRelayCommand PowerOffCommand { get; }

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
        AbortCommand = new AsyncRelayCommand(DoAbort);
        ResetCommand = new AsyncRelayCommand(DoReset);
        PowerOnCommand = new AsyncRelayCommand(DoPowerOn);
        PowerOffCommand = new AsyncRelayCommand(DoPowerOff);
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

        // 源默认选站点表里的第一个（按站点号排，一般是 LoadPort1），目标默认选腔体侧（北）的第一个。
        if (string.IsNullOrEmpty(SourceStation) && Model.StationMarks.Count > 0)
        {
            SourceStation = Model.StationMarks[0].Name;
        }

        if (string.IsNullOrEmpty(TargetStation) && Model.StationMarks.Count > 0)
        {
            var chamber = Model.NorthStations.FirstOrDefault();
            TargetStation = chamber is not null ? chamber.Name : Model.StationMarks[0].Name;
        }

        // 站点槽数可能晚到（后端搬运模块表绑好后才有），每次推送都对一下。
        UpdateSourceSlots();
        UpdateTargetSlots();
    }

    private void UpdateSourceSlots()
    {
        if (FillSlotOptions(SourceSlotOptions, SourceStation))
        {
            SourceSlot = ClampSlot(SourceSlotOptions, SourceSlot);

            // 列表重建时下拉会丢掉选中项，把当前槽位再推一次让它选回来。
            OnPropertyChanged(nameof(SourceSlot));
        }
    }

    private void UpdateTargetSlots()
    {
        if (FillSlotOptions(TargetSlotOptions, TargetStation))
        {
            TargetSlot = ClampSlot(TargetSlotOptions, TargetSlot);
            OnPropertyChanged(nameof(TargetSlot));
        }
    }

    /// <summary>
    /// 槽位下拉按站点槽数列成 1~N；槽数没变不重建（免得下拉被重置），返回是否重建了。
    /// </summary>
    private bool FillSlotOptions(ObservableCollection<int> options, string station)
    {
        int count = 0;
        var mark = Model.StationMarks.FirstOrDefault(candidate => candidate.Name == station);
        if (mark is not null)
        {
            count = mark.SlotCount;
        }

        if (options.Count == count)
        {
            return false;
        }

        options.Clear();
        for (int slot = 1; slot <= count; slot++)
        {
            options.Add(slot);
        }

        return true;
    }

    /// <summary>
    /// 当前槽位在下拉范围里就保留，超出范围回到第一个；没有槽位时原样。
    /// </summary>
    private static int ClampSlot(ObservableCollection<int> options, int slot)
    {
        if (options.Count > 0 && !options.Contains(slot))
        {
            return options[0];
        }

        return slot;
    }

    private async Task DoPick()
    {
        var response = await _service.PickAsync(new RobotPickPlaceRequest
        {
            Module = ModuleName,
            Arm = SelectedArm,
            Station = SourceStation,
            Slot = SourceSlot,
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
            Station = TargetStation,
            Slot = TargetSlot,
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
}
