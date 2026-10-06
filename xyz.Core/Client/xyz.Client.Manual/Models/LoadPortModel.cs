using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz.Client.Manual.Models;


public class LoadPortModel : ObservableObject
{
    private int _state;

    /// <summary>模块状态码，取值见 ModuleState/LoadPortState。</summary>
    public int State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    private ModuleMode _mode;

    /// <summary>模块模式（Online/Offline）：是否参与自动调度。</summary>
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

    private bool _isPodPlaced;

    /// <summary>FOUP 在位（状态查询或 PODON/PODOF 事件）。</summary>
    public bool IsPodPlaced
    {
        get => _isPodPlaced;
        set => SetProperty(ref _isPodPlaced, value);
    }

    private bool? _podPresent;

    /// <summary>查询反馈：FOUP 在位；null 表示反馈不可用。</summary>
    public bool? PodPresent
    {
        get => _podPresent;
        set => SetProperty(ref _podPresent, value);
    }

    private bool? _podPlaced;

    /// <summary>查询反馈：FOUP 放置到位；null 表示反馈不可用。</summary>
    public bool? PodPlaced
    {
        get => _podPlaced;
        set => SetProperty(ref _podPlaced, value);
    }

    private bool? _deviceAlarm;

    /// <summary>查询反馈：设备硬件报警；null 表示反馈不可用。</summary>
    public bool? DeviceAlarm
    {
        get => _deviceAlarm;
        set => SetProperty(ref _deviceAlarm, value);
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
