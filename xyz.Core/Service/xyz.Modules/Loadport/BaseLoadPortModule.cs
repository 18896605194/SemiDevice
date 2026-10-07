using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Drivers.Loadport;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

public abstract class BaseLoadPortModule : BaseTransferStationModule, ILoadPort
{
    #region SV 

    [VariableMark(VariableType.SV, ValueFormat.Int, description: "模块状态码")]
    public override int State { get; protected set; } = ModuleState.NotInit;

    /// <summary>
    /// 载具到了（SV）：按 SC PresenceSource 判出来的结果，不是哪个传感器的原始值——Query 看状态查询的在位（IsPresent）、
    /// 到位（IsPlaced）两位，Event 看 PODON/PODOF；只在扫描线程上改。载具到达、拿走、E84 交接、Job、回片、E87 用的都是它，
    /// 推给界面的"在位"也是它。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "载具到了（在位、到位都亮，或设备报了放上）")]
    public bool IsCarrierArrived { get; private set; }

    /// <summary>
    /// Auto/Manual
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "Auto/Manual（true=Auto，false=Manual）")]
    public bool IsAutoMode { get; private set; }

    private volatile string? _carrierId;

    /// <summary>
    /// 载具 ID
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "当前载具 ID")]
    public string? CarrierId
    {
        get => _carrierId;
        private set => _carrierId = value;
    }

    private volatile IReadOnlyList<SlotState> _slotMap = Array.Empty<SlotState>();

    /// <summary>
    /// 最近一次 Mapping 结果，下标 0 对应第 1 槽；未 Mapping 或载具已移走为空列表。
    /// </summary>
    public IReadOnlyList<SlotState> SlotMap
    {
        get => _slotMap;
        private set => _slotMap = value;
    }

    /// <summary>
    /// loapdort 状态数据，从驱动读取
    /// </summary>
    private volatile LoadPortStatus? _status;

    /// <summary>
    /// 最近一次成功查询的设备状态
    /// </summary>
    public LoadPortStatus? Status
    {
        get => _status;
        protected set => _status = value;
    }
    #endregion

    #region SC 

    [SCEditor("True", "LoadPort", "是否启用本 LoadPort (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("25", "LoadPort", "花篮槽数")]
    public override int SlotCount { get; set; } = 25;

    [SCEditor("True", "LoadPort", "载具到位后自动读码（False=只由上层/EAP 显式触发）")]
    public bool AutoReadCarrierId { get; set; } = true;

    [SCEditor("Query", "LoadPort",
        "载具在位以什么为准：Query = 状态查询（在位、到位两位都亮算放好，都灭算拿走，一亮一灭不算变化）；Event = 设备主动上报（PODON 放上 / PODOF 拿走）")]
    public PodPresenceSource PresenceSource { get; set; } = PodPresenceSource.Query;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "3000", description: "设备状态查询超时")]
    public int QueryDataTimeOut
    {
        get { return GetEcInt(nameof(QueryDataTimeOut)); }
        set { SetEcInt(nameof(QueryDataTimeOut), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "30000", description: "Load 动作超时（开门+Mapping）")]
    public int LoadTimeout
    {
        get { return GetEcInt(nameof(LoadTimeout)); }
        set { SetEcInt(nameof(LoadTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "30000", description: "Unload 动作超时（关门）")]
    public int UnloadTimeout
    {
        get { return GetEcInt(nameof(UnloadTimeout)); }
        set { SetEcInt(nameof(UnloadTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "60000", description: "Home 动作超时（整机回零）")]
    public int HomeTimeout
    {
        get { return GetEcInt(nameof(HomeTimeout)); }
        set { SetEcInt(nameof(HomeTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "10000", description: "Reset 动作超时（复位清错）")]
    public int ResetTimeout
    {
        get { return GetEcInt(nameof(ResetTimeout)); }
        set { SetEcInt(nameof(ResetTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "10000", description: "Abort 动作超时（终止）")]
    public int AbortTimeout
    {
        get { return GetEcInt(nameof(AbortTimeout)); }
        set { SetEcInt(nameof(AbortTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "10000", description: "Clamp 动作超时（夹紧）")]
    public int ClampTimeout
    {
        get { return GetEcInt(nameof(ClampTimeout)); }
        set { SetEcInt(nameof(ClampTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "10000", description: "Unclamp 动作超时（松开）")]
    public int UnclampTimeout
    {
        get { return GetEcInt(nameof(UnclampTimeout)); }
        set { SetEcInt(nameof(UnclampTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Bool, @default: "False",
        description: "是否循环跑片（False = 跑完一轮就停）")]
    public bool IsCycle
    {
        get { return bool.TryParse(GetEcString(nameof(IsCycle)), out var cycle) && cycle; }
        set { SetEc(nameof(IsCycle), value.ToString()); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "1", max: "999999",
        @default: "1", description: "循环跑片总轮数（IsCycle=True 时生效）")]
    public int CycleRunTotal
    {
        get { return GetEcInt(nameof(CycleRunTotal)); }
        set { SetEcInt(nameof(CycleRunTotal), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Enum, @default: "BottomUp",
        description: "取片顺序：BottomUp=从下往上（先取 Slot 1），TopDown=从上往下（先取顶槽）",
        Options = "BottomUp,TopDown")]
    public SlotPickOrder PickOrder
    {
        get
        {
            return Enum.TryParse<SlotPickOrder>(GetEcString(nameof(PickOrder)), true, out var order)
                ? order
                : SlotPickOrder.BottomUp;
        }
        set { SetEc(nameof(PickOrder), value.ToString()); }
    }

    #endregion

    #region Alarm

    [Alarm("LoadPort 初始化超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "LoadPort 初始化未在指定时间内完成", Solution = "检查串口连接、LoadPort 硬件状态及供电")]
    public string InitTimeoutAlarm = nameof(InitTimeoutAlarm);

    [Alarm("LoadPort 受控停止", AlarmCategory.ProcessError, AlarmLevel = AlarmLevel.Alarm1, Description = "LoadPort 进入受控停止状态", Solution = "检查 LoadPort 当前状态并复位")]
    public string ControlledStopAlarm = nameof(ControlledStopAlarm);

    [Alarm("LoadPort 设备报警", AlarmCategory.HardwareError, AlarmLevel = AlarmLevel.Alarm1, Description = "LoadPort 设备本身报警", Solution = "检查 LoadPort 硬件/通讯状态")]
    public string LoadPortDeviceAlarm = nameof(LoadPortDeviceAlarm);

    #endregion

    #region Event

    [EventAttribut("FOUP 到达", Description = "FOUP 从不在位变为在位")]
    public readonly string FoupArrivedEvent = "FoupArrived";

    [EventAttribut("FOUP 移除", Description = "FOUP 从在位变为不在位")]
    public readonly string FoupRemovedEvent = "FoupRemoved";

    #endregion

    #region Component

    /// <summary>
    /// 品牌驱动组件（sc.xml 挂在本模块下的 _driver 子节点，换 Type 即换品牌）；Open 时按类型找到并挂上。
    /// </summary>
    public LoadPortDriverComponent? _driver { get; private set; }

    public RfidDriverComponent? _rfid => FindChild<RfidDriverComponent>();

    /// <summary>
    /// E84 交接组件；没配（本机型没有 E84）或 sc.xml 里 IsEnable=False（本机没接搬运车）为 null——
    /// 端口就当没有 E84：不打开、不每拍推、不读写 IO。
    /// </summary>
    public IE84? E84
    {
        get
        {
            var e84 = FindChild<IE84>();
            return e84 is not null && e84.IsEnable ? e84 : null;
        }
    }

    #endregion

    #region 载具

    private readonly object _carrierGate = new();
    private volatile CarrierInfo? _carrier;

    public CarrierInfo? Carrier => _carrier;

    private void UpdateCarrier(Func<CarrierInfo, CarrierInfo> change)
    {
        lock (_carrierGate)
        {
            var carrier = _carrier;
            if (carrier is null)
            {
                return;
            }

            _carrier = change(carrier) with { UpdatedAt = DateTime.Now };
        }
    }

    #endregion

    protected BaseLoadPortModule()
    {
        RegisterTransitions(LoadPortStateTable.ToModuleTable());
    }

    /// <summary>
    /// 传片环锚点态：LoadPort 已装载（Loaded）即可被机械手服务。
    /// </summary>
    protected override int AnchorState => LoadPortState.Loaded;

    #region 驱动连接

    public override bool Open()
    {
        if (!IsEnable)
        {
            return true;
        }

        var rfid = _rfid;
        bool rfidOpened = rfid is null || rfid.Open();
        if (!rfidOpened)
        {
            LogHelper.Warn(Name, "_rfid 读头连不上：LoadPort 照常能用，读码先读不了，读头在后台按间隔重连");
        }

        var e84 = E84;
        if (e84 is not null && !e84.Open())
        {
            return false;
        }

        WaferManagerComponent.Current?.RegisterLoadPort(Name, SlotCount);

        var driver = FindChild<LoadPortDriverComponent>();
        if (driver is null)
        {
            LogHelper.Error(Name, "sc.xml 未挂品牌驱动组件（_driver 子节点），无法打开");
            return false;
        }

        // 先摘后挂：Open 可重入，保证只挂一份。
        driver.DeviceEvent -= OnDeviceEvent;
        driver.DeviceEvent += OnDeviceEvent;
        _driver = driver;
        bool driverOpened = driver.Open();
        return driverOpened && rfidOpened;
    }

    /// <summary>
    /// 关闭 _rfid 读头与驱动连接；与 Open 成对，宿主退出时调用（当前宿主常驻，暂无调用点）。
    /// </summary>
    public void Close()
    {
        _rfid?.Close();
        _driver?.Close();
    }

    private void OnDeviceEvent(LoadPortDeviceEvent evt)
    {
        // 在驱动路由线程回调，只记下设备说的放上 / 拿走；认不认、算不算到达拿走，由扫描线程按 SC PresenceSource 判。
        switch (evt.Kind)
        {
            case LoadPortDeviceEventKind.PodPresent:
                NotePodEvent(true);
                break;

            case LoadPortDeviceEventKind.PodRemoved:
                NotePodEvent(false);
                break;
        }
    }

    #endregion

    #region 在位判断（SC PresenceSource：状态查询 / 设备上报，二选一）

    private volatile bool _eventCarrierArrived;

    protected void NotePodEvent(bool placed)
    {
        _eventCarrierArrived = placed;
    }

    private bool SenseCarrierArrived()
    {
        //事件
        if (PresenceSource == PodPresenceSource.Event)
        {
            return _eventCarrierArrived;
        }

        var status = Status;
        if (status is null)
        {
            return IsCarrierArrived;
        }

        if (status.IsPresent && status.IsPlaced)
        {
            return true;
        }

        if (!status.IsPresent && !status.IsPlaced)
        {
            return false;
        }

        return IsCarrierArrived;
    }

    #endregion

    #region 设备状态查询（每拍一条：Query 判在位、设备报警、界面的设备反馈都靠它）

    private LoadPortCommand? _statusQuery;
    private readonly Stopwatch _statusQueryWatch = new();

    private bool _isStatusQueryLate;

    private void PollStatus()
    {
        var driver = _driver;
        if (!IsEnable || driver is null)
        {
            return;
        }

        if (!driver.IsConnected)
        {
            Status = null;
            _statusQuery = null;
            return;
        }

        var query = _statusQuery;
        if (query is null)
        {
            query = driver.QueryStatus();
            if (query is not null)
            {
                _statusQuery = query;
                _statusQueryWatch.Restart();
            }

            return;
        }

        if (query.IsCompleted)
        {
            _statusQuery = null;
            var response = query.Response;
            if (response is not null && response.IsSuccess && response.Status is not null)
            {
                Status = response.Status;
                if (_isStatusQueryLate)
                {
                    _isStatusQueryLate = false;
                    LogHelper.Info(Name, "状态查询恢复");
                }
            }

            return;
        }

        int timeout = QueryDataTimeOut;
        if (_statusQueryWatch.ElapsedMilliseconds < timeout)
        {
            return;
        }

        driver.Abandon(query, "Timeout");
        _statusQuery = null;
        Status = null;
        if (!_isStatusQueryLate)
        {
            _isStatusQueryLate = true;
            LogHelper.Warn(Name, $"状态查询超时（{timeout}ms）：这一条作废、接着查，查到之前在位保持原判断");
        }
    }

    #endregion

    #region 状态发布

    private LoadPortDto? _lastPublishedState;

    public LoadPortDto CreateStateDto()
    {
        var carrier = Carrier;
        var dto = new LoadPortDto
        {
            Name = Name,
            State = State,
            Mode = Mode,
            IsCarrierArrived = IsCarrierArrived,
            AutoMode = IsAutoMode,
            CarrierId = CarrierId ?? string.Empty,
            SlotCount = SlotCount,
            Slots = ToSlotDtos(SlotMap),
            LedgerSlots = WaferLedgerSnapshot.SlotsOf(Name),
            HasCarrier = carrier is not null,
            LotId = carrier?.LotId ?? string.Empty,
            CarrierIdStatus = carrier?.IdStatus ?? CarrierIdStatus.NotRead,
            CarrierSlotMapStatus = carrier?.SlotMapStatus ?? CarrierSlotMapStatus.NotRead,
            CarrierAccessStatus = carrier?.AccessStatus ?? CarrierAccessStatus.NotAccessed,
        };

        var driver = _driver;
        if (driver is not null)
        {
            dto.IsConnected = driver.IsConnected;
        }

        var status = Status;
        if (!dto.IsConnected)
        {
            status = null;
        }

        if (!IsEnable)
        {
            status = null;
        }

        if (status is not null)
        {
            dto.IsPresent = status.IsPresent;
            dto.IsPlaced = status.IsPlaced;
            dto.IsDoorOpen = status.IsDoorOpen;
            dto.IsDoorClosed = status.IsDoorClosed;
            dto.IsDeviceAlarm = status.IsDeviceAlarm;
        }

        return dto;
    }

    /// <summary>
    /// Mapping 结果转契约对象：下标 0 即第 1 槽，槽位状态按厂商无关语义原样带出。
    /// </summary>
    private static List<LoadPortSlotDto> ToSlotDtos(IReadOnlyList<SlotState> slotMap)
    {
        var slots = new List<LoadPortSlotDto>(slotMap.Count);
        for (int index = 0; index < slotMap.Count; index++)
        {
            slots.Add(new LoadPortSlotDto
            {
                Slot = index + 1,
                State = slotMap[index] switch
                {
                    SlotState.Empty => LoadPortSlotState.Empty,
                    SlotState.NotEmpty => LoadPortSlotState.NotEmpty,
                    SlotState.CorrectlyOccupied => LoadPortSlotState.CorrectlyOccupied,
                    SlotState.DoubleSlotted => LoadPortSlotState.DoubleSlotted,
                    SlotState.CrossSlotted => LoadPortSlotState.CrossSlotted,
                    _ => LoadPortSlotState.Undefined,
                },
            });
        }

        return slots;
    }

    protected override void PublishState()
    {
        var dto = CreateStateDto();
        if (!dto.HasStateChanged(_lastPublishedState))
        {
            return;
        }

        _lastPublishedState = dto;
        EventBus.Send(dto, Name);
    }

    #endregion

    #region Action（ILoadPort 契约：平台给默认实现——一条驱动指令一个动作；机型有特殊动作再重写）

    private LoadPortAction _action;

    /// <summary>
    /// 发起一次读码。读码要走好几轮握手（几百毫秒），所以这里只发起、不等结果——
    /// 读完之后 CarrierId 会更新，并回调 EAP 的 CarrierIdRead / CarrierIdReadFailed。
    /// 未挂读头组件、读头没连上或上一次还没读完，返回 false。
    /// </summary>
    public bool ReadCarrierId()
    {
        var reader = _rfid;
        return reader is not null && reader.BeginRead();
    }

    /// <summary>
    /// 收读码结果：成功更新 CarrierId 并回调 CarrierIdRead，失败回调 CarrierIdReadFailed。
    /// 在扫描线程上跑（读头的步进机由 base.OnScan 递归带着走，这里紧跟着取结果）。
    /// </summary>
    private void CheckCarrierIdRead()
    {
        var result = _rfid?.TakeResult();
        if (result is null)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            LogHelper.Warn(Name, $"读码失败: {result.Error}");
            UpdateCarrier(carrier => carrier with { IdStatus = CarrierIdStatus.ReadFailed });
            EnqueueE87(callback => callback.CarrierIdReadFailed(this));
            return;
        }

        string carrierId = result.CarrierId;
        CarrierId = carrierId;

        // 读到 ≠ 认定：接了 EAP 的话还要 Host 点头（ProceedWithCarrier）才转 Verified。
        UpdateCarrier(carrier => carrier with { CarrierId = carrierId, IdStatus = CarrierIdStatus.Read });
        WaferManagerComponent.Current?.SetCarrierIdOn(Name, carrierId);
        EnqueueE87(callback => callback.CarrierIdRead(this, carrierId));
    }

    /// <summary>
    /// 改写载具 ID：Host 确认的 ID 与读到的不一致时以 Host 为准（对应 CTC 的 ProceedSetCarrierID）；不回调 EAP。
    /// </summary>
    public void SetCarrierId(string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        CarrierId = carrierId;

        // Host 改写即认定：读到什么不重要了，以 Host 为准。
        UpdateCarrier(carrier => carrier with { CarrierId = carrierId, IdStatus = CarrierIdStatus.Verified });
        WaferManagerComponent.Current?.SetCarrierIdOn(Name, carrierId);
    }

    /// <summary>
    /// EAP 写回 Host 核对载具的进展（给了的那一项才改）；没有载具时什么都不做，不回调 EAP。
    /// </summary>
    public void UpdateCarrierStatus(CarrierIdStatus? idStatus, CarrierSlotMapStatus? slotMapStatus)
    {
        UpdateCarrier(carrier => carrier with
        {
            IdStatus = idStatus ?? carrier.IdStatus,
            SlotMapStatus = slotMapStatus ?? carrier.SlotMapStatus,
        });
    }

    /// <summary>
    /// 更新 Mapping 结果并回调 EAP SlotMapRead；机型在 Mapping 数据到达时调用，空列表忽略。
    /// </summary>
    protected void UpdateSlotMap(IReadOnlyList<SlotState> slotMap)
    {
        ArgumentNullException.ThrowIfNull(slotMap);
        if (slotMap.Count == 0)
        {
            return;
        }

        var snapshot = slotMap.ToArray();
        SlotMap = snapshot;
        UpdateCarrier(carrier => carrier with { SlotMapStatus = CarrierSlotMapStatus.Read });
        ApplySlotMapToLedger(snapshot);
        EnqueueE87(callback => callback.SlotMapRead(this, snapshot));
    }

    /// <summary>
    /// 把 Mapping 结果落到晶圆账：这个端口的账整篮重建，一槽一片。
    ///
    /// 识别不出来的槽（Undefined）按"有片但状态不明"记，不按空槽记——两种错的代价不一样：
    /// 记成有片而实际没有，机械手去取会空手，Verify 对不上报警，停下来让人看；
    /// 记成空而实际有片，机械手会往上放，那是撞片。宁可多记不可漏记。
    /// </summary>
    private void ApplySlotMapToLedger(IReadOnlyList<SlotState> slotMap)
    {
        var ledger = WaferManagerComponent.Current;
        if (ledger is null)
        {
            return;
        }

        ledger.RegisterLocation(Name, SlotCount);

        var statuses = new WaferStatus?[slotMap.Count];
        for (int index = 0; index < slotMap.Count; index++)
        {
            statuses[index] = slotMap[index] switch
            {
                SlotState.Empty => null,
                SlotState.CorrectlyOccupied => WaferStatus.Normal,
                SlotState.NotEmpty => WaferStatus.Normal,
                SlotState.DoubleSlotted => WaferStatus.Double,
                SlotState.CrossSlotted => WaferStatus.Crossed,
                _ => WaferStatus.Unknown,
            };
        }

        var carrier = Carrier;
        int created = ledger.ApplySlotMap(Name, statuses, carrier?.CarrierId, carrier?.LotId);
        LogHelper.Info($"[{Name}] Mapping 落账：{created} 片");
    }

    /// <summary>
    /// 发起 Load（开门 + Mapping）：平台默认发驱动的 Load 指令，成功后把 Mapping 结果落下来（UpdateSlotMap）。
    /// 机型的 Load 要多做别的步骤就重写。
    /// </summary>
    public virtual ModuleOperation? Load()
    {
        return Begin(LoadPortAction.Load, new LoadPortCommandOperation("Load", () => _driver?.Load(), () => LoadTimeout,
            response => UpdateSlotMap(response.SlotMap)));
    }

    /// <summary>
    /// 发起 Unload（关门）：平台默认发驱动的 Unload 指令。
    /// </summary>
    public virtual ModuleOperation? Unload()
    {
        return Begin(LoadPortAction.Unload, new LoadPortCommandOperation("Unload", () => _driver?.Unload(), () => UnloadTimeout));
    }

    /// <summary>
    /// 发起 Home（整机回零）：平台默认发驱动的 Home 指令。
    /// </summary>
    public virtual ModuleOperation? Home()
    {
        return Begin(LoadPortAction.Home, new LoadPortCommandOperation("Home", () => _driver?.Home(), () => HomeTimeout));
    }

    /// <summary>
    /// 初始化（重写组件基类的 Init）：先初始化子组件（E84、_rfid），再回原点——Home 就是 LoadPort 的初始化，
    /// 超时报的也是"初始化超时"。返回 Home 操作，调用方等它做完；状态不允许时为 null。
    /// </summary>
    public override ModuleOperation? Init()
    {
        base.Init();
        return Home();
    }

    /// <summary>
    /// 复位（重写组件基类的 Reset）：先清报警、复位子组件（E84、_rfid），再发设备复位清错。
    /// 返回设备复位操作，调用方等它做完；状态不允许时为 null，报警照样已经清了。
    /// </summary>
    public override ModuleOperation? Reset()
    {
        base.Reset();
        return ResetDevice();
    }

    /// <summary>
    /// 发设备复位清错：平台默认发驱动的 ResetDrive 指令。
    /// </summary>
    protected virtual ModuleOperation? ResetDevice()
    {
        return Begin(LoadPortAction.Reset, new LoadPortCommandOperation("Reset", () => _driver?.ResetDrive(), () => ResetTimeout));
    }

    /// <summary>
    /// 中止（重写组件基类的 Abort）：先中止子组件，再发设备中止；Abort 可顶替在途动作，不清报警。
    /// 返回设备中止操作；状态不允许时为 null。
    /// </summary>
    public override ModuleOperation? Abort()
    {
        base.Abort();
        return AbortDevice();
    }

    /// <summary>
    /// 发设备中止（急停）：平台默认发驱动的 Stop 指令。
    /// </summary>
    protected virtual ModuleOperation? AbortDevice()
    {
        return Begin(LoadPortAction.Abort, new LoadPortCommandOperation("Abort", () => _driver?.Stop(), () => AbortTimeout));
    }

    /// <summary>
    /// 发起 Clamp（夹紧 FOUP），状态表只允许空闲时发起：平台默认发驱动的 Clamp 指令。
    /// </summary>
    public virtual ModuleOperation? Clamp()
    {
        return Begin(LoadPortAction.Clamp, new LoadPortCommandOperation("Clamp", () => _driver?.Clamp(), () => ClampTimeout));
    }

    /// <summary>
    /// 发起 Unclamp（松开 FOUP），状态表只允许空闲时发起：平台默认发驱动的 Unclamp 指令。
    /// </summary>
    public virtual ModuleOperation? Unclamp()
    {
        return Begin(LoadPortAction.Unclamp, new LoadPortCommandOperation("Unclamp", () => _driver?.Unclamp(), () => UnclampTimeout));
    }

    /// <summary>
    /// 切 Auto/Manual（LoadPort 的 Access Mode，内部模式位，不经设备协议）；置位后由下一次扫描随状态事件发布，
    /// E84 组件下一拍按它开关与搬运车的交接。模式有变化时回调 EAP AutoModeChanged。
    /// </summary>
    public void SetAutoMode(bool autoMode)
    {
        if (IsAutoMode == autoMode)
        {
            return;
        }

        IsAutoMode = autoMode;
        EnqueueE87(callback => callback.AutoModeChanged(this, autoMode));
    }

    /// <summary>
    /// 机械手要来取放片（交互环的准备一）：这个载具第一次被取放时进 E87 的 IN ACCESS，回调 EAP AccessStarted。
    /// </summary>
    public override ModuleOperation? PrepareTransfer()
    {
        var operation = base.PrepareTransfer();
        if (operation is not null)
        {
            MarkInAccess();
        }

        return operation;
    }

    private void MarkInAccess()
    {
        bool started = false;
        lock (_carrierGate)
        {
            var carrier = _carrier;
            if (carrier is not null && carrier.AccessStatus == CarrierAccessStatus.NotAccessed)
            {
                _carrier = carrier with { AccessStatus = CarrierAccessStatus.InAccess, UpdatedAt = DateTime.Now };
                started = true;
            }
        }

        if (started)
        {
            EnqueueE87(callback => callback.AccessStarted(this));
        }
    }

    /// <summary>
    /// 装机停用、或驱动还没建起来（装配里 Open 失败）都不发动作。
    /// </summary>
    protected override bool CanBeginAction => IsEnable && _driver is not null;

    /// <summary>
    /// 发起动作：发起这件事走基类，这儿只多记一笔"这趟发的是什么动作"——终结时按它回调 EAP。
    /// 记在同一把锁里：否则扫描线程可能在记上之前就把操作终结了，回调就发错。
    /// </summary>
    protected ModuleOperation? Begin(LoadPortAction action, ModuleOperation operation)
    {
        lock (OperationGate)
        {
            var started = base.Begin(action, operation);
            if (started is not null)
            {
                _action = action;
            }

            return started;
        }
    }

    /// <summary>
    /// 操作终结（状态已由基类落好）：失败的动作报警，
    /// 成功的动作与失败的原因都回调 EAP（在模块锁内只入队，派发在扫描线程锁外进行）。
    /// 没做成（失败、超时、被中止顶替）的，把驱动上还在等回复的指令全部作废：它的回复多半丢了，
    /// 不作废的话同名指令一直占着在途位，以后再也发不出去。在途的状态查询一起作废也没关系，下一拍重发。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        UpdateActionAlarms(operation);

        if (!operation.IsSuccess)
        {
            _driver?.AbandonAll("Abandoned");
            string reason = operation.Reason;

            // 取放途中出错：这个载具算没干完，落 Stopped（还没开始取放的就不动它）。
            UpdateCarrier(carrier => carrier.AccessStatus == CarrierAccessStatus.InAccess
                ? carrier with { AccessStatus = CarrierAccessStatus.Stopped }
                : carrier);
            EnqueueE87(callback => callback.PortError(this, reason));
            return;
        }

        switch (_action)
        {
            case LoadPortAction.Load:
                // 门已开、槽图读了。还不算开始取放（E87 IN ACCESS 是机械手第一次来取放片，见 PrepareTransfer）：
                // 这时候 Host 核对槽图不通过还能取消这个载具。
                EnqueueE87(callback => callback.LoadCompleted(this));
                break;

            case LoadPortAction.Unload:
                // 门已关，取放结束。干完没干完不由这里判——上层作业调 NoteCarrierComplete 才算完成；
                // 取放过、没判完成就 Unload 了，这个载具算中断（E87 CARRIER STOPPED），已经 Complete/Stopped 的保持原样。
                UpdateCarrier(carrier => carrier.AccessStatus == CarrierAccessStatus.InAccess
                    ? carrier with { AccessStatus = CarrierAccessStatus.Stopped }
                    : carrier);
                EnqueueE87(callback => callback.AccessStopped(this));
                EnqueueE87(callback => callback.UnloadCompleted(this));
                break;

            case LoadPortAction.Home:
                EnqueueE87(callback => callback.Homed(this));
                break;

            case LoadPortAction.Clamp:
                EnqueueE87(callback => callback.ClampCompleted(this));
                break;

            case LoadPortAction.Unclamp:
                EnqueueE87(callback => callback.UnclampCompleted(this));
                break;
        }
    }

    #endregion

    #region EAP 口子（设备侧上报给 EAP，EAP 经 ILoadPort 反向下发动作）

    /// <summary>
    /// E87 载具管理回调；null 表示未接 EAP，模块照常运行。装配时由 EAP 侧挂上。
    /// </summary>
    public IE87Callback? E87Callback { get; set; }

    /// <summary>
    /// E84 自动交接回调；null 表示未接 EAP 或本机没有 E84 硬件。
    /// </summary>
    public IE84Callback? E84Callback { get; set; }

    /// <summary>
    /// E84 握手期间反查 EAP 的口子；null 时设备侧按本地开关自行决定。
    /// </summary>
    public IE84Provider? E84Provider { get; set; }

    #region E84（端口驱动 E84 组件：每拍给许可与载具在位，交接进展转给 EAP）

    /// <summary>
    /// 推 E84 一拍，交接进展放进 EAP 的派发组件（跟所有上报同一条，先后不乱）；没配 E84 组件什么都不做。
    /// </summary>
    private void StepE84()
    {
        var e84 = E84;
        if (e84 is null)
        {
            return;
        }

        foreach (var report in e84.Step(CurrentE84Permit(), IsCarrierArrived))
        {
            EnqueueE84(callback => report.DispatchTo(callback, this));
        }
    }

    /// <summary>
    /// 这一拍能不能交接、往哪个方向：接了 EAP 以 EAP 的 Access Mode 与搬运状态为准，没接由端口本地判断。
    /// </summary>
    private E84Permit CurrentE84Permit()
    {
        bool auto = E84Provider?.IsAutoAccessMode(this) ?? IsAutoMode;
        if (!auto)
        {
            return E84Permit.NotAvailable;
        }

        return (E84Provider?.GetTransferState(this) ?? LocalTransferState) switch
        {
            LoadPortTransferState.OutOfService => E84Permit.NotAvailable,
            LoadPortTransferState.ReadyToLoad => E84Permit.ReadyToLoad,
            LoadPortTransferState.ReadyToUnload => E84Permit.ReadyToUnload,
            _ => E84Permit.Blocked,
        };
    }

    /// <summary>
    /// 端口自己判的搬运状态（没接 EAP 时 E84 就按它；接了 EAP 由 E87 在它上面加 Host 的设定）：
    /// 停用、下线、未初始化或出错 → Out Of Service；不在空闲 → 挡住；
    /// 空闲且没载具 → 等送盒；有载具且这一盒已经干完或中断（Complete/Stopped）→ 等取走；其余挡住。
    /// </summary>
    public LoadPortTransferState LocalTransferState
    {
        get
        {
            if (!IsEnable || Mode != ModuleMode.Online || State == ModuleState.NotInit || State == ModuleState.Error)
            {
                return LoadPortTransferState.OutOfService;
            }

            if (State != ModuleState.Idle)
            {
                return LoadPortTransferState.TransferBlocked;
            }

            if (!IsCarrierArrived)
            {
                return LoadPortTransferState.ReadyToLoad;
            }

            return Carrier?.AccessStatus is CarrierAccessStatus.Complete or CarrierAccessStatus.Stopped
                ? LoadPortTransferState.ReadyToUnload
                : LoadPortTransferState.TransferBlocked;
        }
    }

    /// <summary>载具 Load 好了：Loaded，或者正被机械手服务（交互环的几个状态）。</summary>
    public bool IsLoaded
    {
        get
        {
            int state = State;
            return state == LoadPortState.Loaded
                || (state >= TransferModuleState.PreTransfer && state <= TransferModuleState.TransferComplete);
        }
    }

    /// <summary>端口空闲（Idle）：不在做动作、不在被机械手服务。</summary>
    public bool IsIdle => State == ModuleState.Idle;

    #endregion

    /// <summary>
    /// 上层作业判定这个载具干完了：转成 E87 的 CarrierComplete 上报。
    /// </summary>
    public void NoteCarrierComplete()
    {
        UpdateCarrier(carrier => carrier with { AccessStatus = CarrierAccessStatus.Complete });
        EnqueueE87(callback => callback.CarrierComplete(this));
    }

    /// <summary>
    /// 扫描周期：先扫子组件与操作（基类，读头的读码步进机、驱动的断线重连也在里面），
    /// 再查设备状态、判载具在位边沿、推 E84、收读码结果、查设备报警，最后有变化就推给界面；
    /// EAP 上报放进 EAP 的派发组件发，不占扫描线程。机型重写时先调 base.OnScan()。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        PollStatus();
        CheckCarrierPresence();
        StepE84();
        CheckCarrierIdRead();
        CheckDeviceAlarm();
        PublishState();
    }

    /// <summary>
    /// 设备报警跟着状态查询走：报警位亮就报。灭了也不清——报警只能人工 Reset 清。
    /// 查不到（没连上/查询超时，Status 为 null）不判。
    /// </summary>
    private void CheckDeviceAlarm()
    {
        var status = Status;
        if (status is not null && status.IsDeviceAlarm)
        {
            RaiseAlarm(LoadPortDeviceAlarm);
        }
    }

    /// <summary>
    /// 动作类报警：失败就报，Home 超时再加报初始化超时；动作成功也不清，只能人工 Reset 清。
    /// 人为急停顶掉的不报——那是操作员自己按的，不是故障。
    /// </summary>
    private void UpdateActionAlarms(ModuleOperation operation)
    {
        if (operation.IsSuccess || operation.Code == ErrorCodes.Aborted)
        {
            return;
        }

        RaiseAlarm(ControlledStopAlarm);
        if (_action == LoadPortAction.Home && operation.Code == ErrorCodes.Timeout)
        {
            RaiseAlarm(InitTimeoutAlarm);
        }
    }

    /// <summary>
    /// 检查载具是不是在位，两个信号
    /// </summary>
    private void CheckCarrierPresence()
    {
        bool placed = SenseCarrierArrived();
        if (placed == IsCarrierArrived)
        {
            return;
        }

        IsCarrierArrived = placed;
        if (placed)
        {
            lock (_carrierGate)
            {
                _carrier = new CarrierInfo { Location = Name, Capacity = SlotCount };
            }

            EnqueueE87(callback => callback.CarrierArrived(this));
            RaiseEvent(FoupArrivedEvent);
            if (AutoReadCarrierId)
            {
                ReadCarrierId();
            }

            return;
        }

        string? carrierId = CarrierId;
        CarrierId = null;
        SlotMap = Array.Empty<SlotState>();
        lock (_carrierGate)
        {
            _carrier = null;
        }

        // 载具走了，这个端口上的片也一起走：晶圆账上清掉，免得留一堆幽灵片。
        WaferManagerComponent.Current?.Clear(Name);
        EnqueueE87(callback => callback.CarrierRemoved(this, carrierId));
        RaiseEvent(FoupRemovedEvent);
    }

    /// <summary>
    /// 一条 E87 上报放进 EAP 的派发组件；未挂 EAP 时直接丢弃。任意线程可调。
    /// </summary>
    private void EnqueueE87(Action<IE87Callback> notification)
    {
        if (E87Callback is null)
        {
            return;
        }

        EapNotifierComponent.Current?.Post(() =>
        {
            var callback = E87Callback;
            if (callback is not null)
            {
                notification(callback);
            }
        });
    }

    /// <summary>
    /// 一条 E84 上报放进 EAP 的派发组件；未挂 EAP 时直接丢弃。任意线程可调（E84 信号由 IO 线程翻转）。
    /// </summary>
    protected void EnqueueE84(Action<IE84Callback> notification)
    {
        if (E84Callback is null)
        {
            return;
        }

        EapNotifierComponent.Current?.Post(() =>
        {
            var callback = E84Callback;
            if (callback is not null)
            {
                notification(callback);
            }
        });
    }

    #endregion
}
