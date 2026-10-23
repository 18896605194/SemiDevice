using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Manual.Models;


public class LoadPortModel : ObservableObject
{
    /// <summary>装载中、已装载的状态码（同 xyz.Modules 的 LoadPortState）。</summary>
    private const int LoadingState = 100;

    private const int LoadedState = 110;

    private int _state;

    /// <summary>模块状态码，取值见 ModuleState/LoadPortState。</summary>
    public int State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    private ModuleMode _mode;

    /// <summary>
    /// 模块模式（生产 = 交给调度用 / 维护）
    /// </summary>
    public ModuleMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }

    private bool _isConnected;

    /// <summary>驱动串口连接是否可用。</summary>
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private bool? _isPresent;

    /// <summary>查询反馈：FOUP 在位；null 表示反馈不可用。</summary>
    public bool? IsPresent
    {
        get => _isPresent;
        set => SetProperty(ref _isPresent, value);
    }

    private bool? _isPlaced;

    /// <summary>查询反馈：FOUP 放置到位；null 表示反馈不可用。</summary>
    public bool? IsPlaced
    {
        get => _isPlaced;
        set => SetProperty(ref _isPlaced, value);
    }

    private bool? _isDoorOpen;

    /// <summary>查询反馈：门开到位；null 表示反馈不可用。</summary>
    public bool? IsDoorOpen
    {
        get => _isDoorOpen;
        set => SetProperty(ref _isDoorOpen, value);
    }

    private bool? _isDoorClosed;

    /// <summary>查询反馈：门关到位；null 表示反馈不可用。</summary>
    public bool? IsDoorClosed
    {
        get => _isDoorClosed;
        set => SetProperty(ref _isDoorClosed, value);
    }

    /// <summary>三维图：门在半路——开到位、关到位两个反馈都灭（命令发了还没到位）；查不到反馈不算。</summary>
    public bool IsDoorUnknown => IsDoorOpen == false && IsDoorClosed == false;

    /// <summary>三维图：门画成开着——有门开到位反馈就照反馈；查不到反馈（没连上、停用）按状态，已装载 = 门开。</summary>
    public bool IsDoorShownOpen => IsDoorOpen ?? State == LoadedState;

    /// <summary>
    /// 三维图：托板推进去了（对接）。设备没有对接到位反馈，按状态和门推：装载中、已装载算推进去；门开着或在半路一定是推进去的。
    /// 所以卸载时门关到位以后才画退出，跟设备"先关门、再退出"的先后一样。
    /// </summary>
    public bool IsDocked => State == LoadingState || State == LoadedState || IsDoorShownOpen || IsDoorUnknown;

    private bool? _isDeviceAlarm;

    /// <summary>查询反馈：设备硬件报警；null 表示反馈不可用。</summary>
    public bool? IsDeviceAlarm
    {
        get => _isDeviceAlarm;
        set => SetProperty(ref _isDeviceAlarm, value);
    }

    private bool _autoMode;

    /// <summary>Auto/Manual（LoadPort 的 Access Mode）：true = Auto（搬运车经 E84 自动交接），false = Manual（人工放取）。</summary>
    public bool AutoMode
    {
        get => _autoMode;
        set => SetProperty(ref _autoMode, value);
    }

    private string _carrierId = string.Empty;

    /// <summary>载具 ID；未读到为空串。</summary>
    public string CarrierId
    {
        get => _carrierId;
        set => SetProperty(ref _carrierId, value);
    }

    private int _slotCount;

    /// <summary>花篮槽数（后端 sc.xml 里 LoadPort 节点的 SlotCount）；还没收到状态时为 0，界面不画槽。</summary>
    public int SlotCount
    {
        get => _slotCount;
        set => SetProperty(ref _slotCount, value);
    }

    private List<LoadPortSlotDto> _slots = [];

    /// <summary>花篮槽位表（Mapping 结果），下标顺序即槽位顺序。</summary>
    public List<LoadPortSlotDto> Slots
    {
        get => _slots;
        set
        {
            if (SetProperty(ref _slots, value))
            {
                _wafers = null;
            }
        }
    }

    private List<WaferSlotDto> _ledgerSlots = [];

    /// <summary>晶圆账上各槽的片；晶圆账没开或没登记这个 LoadPort 时为空（这时按 Mapping 结果画）。</summary>
    public List<WaferSlotDto> LedgerSlots
    {
        get => _ledgerSlots;
        set
        {
            if (SetProperty(ref _ledgerSlots, value))
            {
                _wafers = null;
            }
        }
    }

    private IReadOnlyList<WaferModel>? _wafers;

    /// <summary>
    /// 花篮里的片：有片的槽位各给一片，交给 LoadPort 控件按槽位号摆（以晶圆账为准，见 StationWafers）。
    /// Model 每次整体替换，绑定随 Model 属性变化重新取值，无需单独通知。
    /// </summary>
    public IReadOnlyList<WaferModel> Wafers => _wafers ??= StationWafers.OfLoadPort(LedgerSlots, Slots);

    /// <summary>
    /// 状态文字（按当前语言）。Model 每次整体替换，绑定随 Model 属性变化重新取值，无需单独通知。
    /// </summary>
    public string StateText => ModuleStates.LoadPortText(State);

    /// <summary>
    /// 状态色调（状态徽标的底色）。
    /// </summary>
    public ModuleStateTone StateTone => ModuleStates.LoadPortTone(State);
}
