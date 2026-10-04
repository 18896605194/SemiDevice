using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 调度图上的一个站点：设备站点号 + 名称 + 所在方位 + 类型（机械手站点表推来，不变），
/// 外加站点模块自己的实时状态和站点上的片（调度图按站点名订阅 LoadPort / 腔体的状态推送，就地刷新；片以晶圆账为准）。
/// </summary>
public sealed class RobotStationModel : ObservableObject
{
    public string Name { get; init; } = string.Empty;

    /// <summary>设备站点号（sc.xml Number，如 LoadPort1=1、Chamber1=3）。</summary>
    public int Number { get; init; }

    /// <summary>机械手伸出方向（sc.xml Direction）：调度图按它把卡片摆在机械手的上、下、左、右。</summary>
    public RobotDirection Direction { get; init; }

    /// <summary>机械手伸出距离（sc.xml Y，数值）。</summary>
    public double Y { get; init; }

    /// <summary>站点槽数（站点模块在 sc.xml 里配的 SlotCount：LoadPort 25、腔体 1），取放槽位下拉按它列 1~N。</summary>
    public int SlotCount { get; init; }

    /// <summary>这个站点允许用的手指号（sc.xml 站点节点的 Arms，没配就是所有手指），取放手臂下拉只列这些。</summary>
    public IReadOnlyList<int> Arms { get; init; } = [];

    /// <summary>站点是哪一类模块：调度图按它选卡片（LoadPort 画花篮卡片，腔体和其他站点画单片卡片）。</summary>
    public StationKind Kind { get; init; }

    /// <summary>角标主文案：站点号。</summary>
    public string NumberText => Number.ToString();

    /// <summary>角标副文案：站点名。</summary>
    public string Title => Name;

    private string _stateText = string.Empty;

    /// <summary>站点模块的状态文字；还没收到状态推送为空（卡片上不显示徽标）。</summary>
    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    private ModuleStateTone _stateTone;

    /// <summary>站点模块的状态色调。</summary>
    public ModuleStateTone StateTone
    {
        get => _stateTone;
        private set => SetProperty(ref _stateTone, value);
    }

    private WaferModel? _wafer;

    /// <summary>腔体站点上的片（晶圆账）；空片位或不是腔体为 null。调度图的腔体卡片画它。</summary>
    public WaferModel? Wafer
    {
        get => _wafer;
        private set => SetProperty(ref _wafer, value);
    }

    private string _recipe = string.Empty;

    /// <summary>腔体最近一次发起成功的工艺配方名；还没做过工艺为空。</summary>
    public string Recipe
    {
        get => _recipe;
        private set => SetProperty(ref _recipe, value);
    }

    private IReadOnlyList<WaferModel> _wafers = [];

    /// <summary>LoadPort 站点花篮里的片（以晶圆账为准）；调度图的 LoadPort 卡片画它。</summary>
    public IReadOnlyList<WaferModel> Wafers
    {
        get => _wafers;
        private set => SetProperty(ref _wafers, value);
    }

    private bool _isConnected;

    /// <summary>LoadPort 驱动连着没有（卡片底栏的"通讯"灯）。</summary>
    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    private bool _isPresent;

    /// <summary>LoadPort 上有 FOUP（"在位"灯）；反馈不可用算没有。</summary>
    public bool IsPresent
    {
        get => _isPresent;
        private set => SetProperty(ref _isPresent, value);
    }

    private bool _isPlaced;

    /// <summary>FOUP 放到位（"到位"灯）；反馈不可用算没有。</summary>
    public bool IsPlaced
    {
        get => _isPlaced;
        private set => SetProperty(ref _isPlaced, value);
    }

    private bool _hasDeviceAlarm;

    /// <summary>LoadPort 设备硬件报警（"报警"灯）；反馈不可用算没有。</summary>
    public bool HasDeviceAlarm
    {
        get => _hasDeviceAlarm;
        private set => SetProperty(ref _hasDeviceAlarm, value);
    }

    private bool _isAutoMode;

    /// <summary>LoadPort 的 Access Mode：true = Auto（"自动"灯），false = Manual（"手动"灯）。</summary>
    public bool IsAutoMode
    {
        get => _isAutoMode;
        private set => SetProperty(ref _isAutoMode, value);
    }

    /// <summary>
    /// 用 LoadPort 的状态推送刷新（界面线程调用）：状态字和色调按 LoadPort 的码归，花篮按账画，底栏几盏灯跟设备反馈走。
    /// </summary>
    public void UpdateLoadPort(LoadPortDto dto)
    {
        StateText = ModuleStates.LoadPortText(dto.State);
        StateTone = ModuleStates.LoadPortTone(dto.State);
        Wafers = StationWafers.OfLoadPort(dto.LedgerSlots, dto.Slots);
        IsConnected = dto.IsConnected;
        IsPresent = dto.PodPresent == true;
        IsPlaced = dto.PodPlaced == true;
        HasDeviceAlarm = dto.DeviceAlarm == true;
        IsAutoMode = dto.AutoMode;
    }

    /// <summary>
    /// 用腔体的状态推送刷新（界面线程调用）：状态字和色调按腔体的码归（同一个码在 LoadPort 和腔体里意思不同），片按账画。
    /// </summary>
    public void UpdateChamber(ChamberDto dto)
    {
        StateText = ModuleStates.ChamberText(dto.State);
        StateTone = ModuleStates.ChamberTone(dto.State);
        var slot = dto.Slots.FirstOrDefault(item => item.HasWafer);
        Wafer = slot is null ? null : StationWafers.OfChamber(slot);
        Recipe = dto.Recipe ?? string.Empty;
    }
}
