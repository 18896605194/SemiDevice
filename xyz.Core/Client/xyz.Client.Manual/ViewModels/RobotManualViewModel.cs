using System.Collections.ObjectModel;
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
/// 机械手手动操作面板 ViewModel：按钮发指令，状态靠订阅刷新；站点表（含各站点槽数、允许的手指）/ 方位 / 平移 / 轴坐标都是后端推的，这里不写死。
/// Pick 用源手臂从源站点槽位取片、Place 用目标手臂往目标站点槽位放片——手臂和槽位下拉都跟着选中的站点走；
/// Abort = 急停（AbortAsync，可顶替在途动作）、Reset = 清报警 + 设备复位清错（ResetAsync）。
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

    private string _sourceStation = string.Empty;

    /// <summary>源站点（Pick 从这儿取片），站点表里的模块名如 LoadPort1；换站点时源手臂、源槽位下拉跟着换。</summary>
    public string SourceStation
    {
        get => _sourceStation;
        set
        {
            if (SetProperty(ref _sourceStation, value))
            {
                UpdateSourceOptions();
            }
        }
    }

    private int _sourceArm = 1;

    /// <summary>Pick 用的手指号（1 开始），只能在源站点允许的手指里选。</summary>
    public int SourceArm
    {
        get => _sourceArm;
        set => SetProperty(ref _sourceArm, value);
    }

    private int _sourceSlot = 1;

    /// <summary>源槽位号。</summary>
    public int SourceSlot
    {
        get => _sourceSlot;
        set => SetProperty(ref _sourceSlot, value);
    }

    private string _targetStation = string.Empty;

    /// <summary>目标站点（Place 往这儿放片）；换站点时目标手臂、目标槽位下拉跟着换。</summary>
    public string TargetStation
    {
        get => _targetStation;
        set
        {
            if (SetProperty(ref _targetStation, value))
            {
                UpdateTargetOptions();
            }
        }
    }

    private int _targetArm = 1;

    /// <summary>Place 用的手指号，只能在目标站点允许的手指里选。</summary>
    public int TargetArm
    {
        get => _targetArm;
        set => SetProperty(ref _targetArm, value);
    }

    private int _targetSlot = 1;

    /// <summary>目标槽位号。</summary>
    public int TargetSlot
    {
        get => _targetSlot;
        set => SetProperty(ref _targetSlot, value);
    }

    /// <summary>源手臂下拉：源站点允许的手指（sc.xml 机械手站点节点的 Arms，没配就是所有手指）。</summary>
    public ObservableCollection<int> SourceArmOptions { get; } = new();

    /// <summary>源槽位下拉：1~源站点的槽数（站点模块在 sc.xml 里配的 SlotCount，LoadPort 25、腔体 1）。</summary>
    public ObservableCollection<int> SourceSlotOptions { get; } = new();

    /// <summary>目标手臂下拉：目标站点允许的手指。</summary>
    public ObservableCollection<int> TargetArmOptions { get; } = new();

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

    /// <summary>调度图站点卡片的状态订阅：每个站点订 LoadPort、腔体两种推送；站点表换了就重订。</summary>
    private readonly List<IDisposable> _stationSubscriptions = new();

    /// <summary>当前订着的是哪一份站点表（Model 在站点表没变时不换实例，按引用比）。</summary>
    private IReadOnlyList<RobotStationModel>? _subscribedStations;

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
        UnsubscribeStations();
    }

    private void OnStateReceived(RobotDto dto)
    {
        ApplyState(dto);
    }

    private void ApplyState(RobotDto dto)
    {
        Model.Update(dto);
        SubscribeStations();

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

        // 站点槽数、允许的手指可能晚到（后端搬运模块表绑好、驱动起来后才齐），每次推送都对一下。
        UpdateSourceOptions();
        UpdateTargetOptions();
    }

    private void UpdateSourceOptions()
    {
        var mark = FindStation(SourceStation);

        if (FillOptions(SourceArmOptions, ArmsOf(mark)))
        {
            SourceArm = ClampOption(SourceArmOptions, SourceArm);

            // 列表重建时下拉会丢掉选中项，把当前值再推一次让它选回来。
            OnPropertyChanged(nameof(SourceArm));
        }

        if (FillOptions(SourceSlotOptions, SlotsOf(mark)))
        {
            SourceSlot = ClampOption(SourceSlotOptions, SourceSlot);
            OnPropertyChanged(nameof(SourceSlot));
        }
    }

    private void UpdateTargetOptions()
    {
        var mark = FindStation(TargetStation);

        if (FillOptions(TargetArmOptions, ArmsOf(mark)))
        {
            TargetArm = ClampOption(TargetArmOptions, TargetArm);
            OnPropertyChanged(nameof(TargetArm));
        }

        if (FillOptions(TargetSlotOptions, SlotsOf(mark)))
        {
            TargetSlot = ClampOption(TargetSlotOptions, TargetSlot);
            OnPropertyChanged(nameof(TargetSlot));
        }
    }

    /// <summary>
    /// 按站点名（就是模块名，也是 EventBus token）订阅站点模块的状态推送，刷新调度图卡片上的状态徽标和片（以晶圆账为准）。
    /// LoadPort、腔体两种都订，站点是哪种就只会来哪种；总线留存最后一条状态，订上立即补发。站点表没换就不重订。
    /// </summary>
    private void SubscribeStations()
    {
        var stations = Model.StationMarks;
        if (ReferenceEquals(stations, _subscribedStations))
        {
            return;
        }

        UnsubscribeStations();
        _subscribedStations = stations;

        foreach (var station in stations)
        {
            _stationSubscriptions.Add(EventBus.Register<LoadPortDto>(station.Name, port =>
            {
                station.UpdateState(ModuleStates.LoadPortText(port.State), ModuleStates.LoadPortTone(port.State));
                station.UpdateWafers(LoadPortModel.WafersOf(port.LedgerSlots, port.Slots));
            }));
            _stationSubscriptions.Add(EventBus.Register<ChamberDto>(station.Name, chamber =>
            {
                station.UpdateState(ModuleStates.ChamberText(chamber.State), ModuleStates.ChamberTone(chamber.State));
                var slot = chamber.Slots.FirstOrDefault(item => item.HasWafer);
                station.UpdateWafer(slot is null ? null : ChamberModel.ToWafer(slot));
            }));
        }
    }

    private void UnsubscribeStations()
    {
        foreach (var subscription in _stationSubscriptions)
        {
            subscription.Dispose();
        }

        _stationSubscriptions.Clear();
        _subscribedStations = null;
    }

    private RobotStationModel? FindStation(string station)
    {
        return Model.StationMarks.FirstOrDefault(candidate => candidate.Name == station);
    }

    /// <summary>
    /// 站点允许的手指（sc.xml 站点节点的 Arms，后端已把"没配"展开成所有手指）；站点还没选上时为空。
    /// </summary>
    private static IReadOnlyList<int> ArmsOf(RobotStationModel? mark)
    {
        if (mark is null)
        {
            return [];
        }

        return mark.Arms;
    }

    /// <summary>
    /// 站点槽位 1~槽数；站点还没选上时为空。
    /// </summary>
    private static IReadOnlyList<int> SlotsOf(RobotStationModel? mark)
    {
        if (mark is null)
        {
            return [];
        }

        return [.. Enumerable.Range(1, mark.SlotCount)];
    }

    /// <summary>
    /// 下拉按给定的值重列；跟现有的一样就不重建（免得下拉被重置），返回是否重建了。
    /// </summary>
    private static bool FillOptions(ObservableCollection<int> options, IReadOnlyList<int> values)
    {
        if (options.SequenceEqual(values))
        {
            return false;
        }

        options.Clear();
        foreach (int value in values)
        {
            options.Add(value);
        }

        return true;
    }

    /// <summary>
    /// 当前值在下拉范围里就保留，超出范围回到第一个；下拉为空时原样。
    /// </summary>
    private static int ClampOption(ObservableCollection<int> options, int value)
    {
        if (options.Count > 0 && !options.Contains(value))
        {
            return options[0];
        }

        return value;
    }

    private async Task DoPick()
    {
        var response = await _service.PickAsync(new RobotPickPlaceRequest
        {
            Module = ModuleName,
            Arm = SourceArm,
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
            Arm = TargetArm,
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
