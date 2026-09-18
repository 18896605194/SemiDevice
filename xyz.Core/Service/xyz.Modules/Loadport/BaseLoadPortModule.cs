using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Wafers;
using xyz.Drivers.Communication;
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

    /// <summary>
    /// LoadPort 当前状态（SV）。既可以保存平台公共状态，也可以保存 LoadPort 专属状态。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "模块状态码")]
    public override int State { get; protected set; } = ModuleState.NotInit;

    /// <summary>
    /// FOUP 是否在位（SV）。驱动 PODON/PODOF 主动事件刷新。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "FOUP 是否在位")]
    public bool IsPodPlaced { get; private set; }

    /// <summary>
    /// Auto/Manual（SV）：LoadPort 独有的 Access Mode，Auto = 搬运车经 E84 自动交接，Manual = 人工放取。
    /// 内部控制位，不经设备协议：SetAutoMode 置位；默认 Manual，掉电不保持。
    /// 跟模块的 Online/Offline（Mode）互不影响；E84 组件按它决定跟不跟搬运车交接。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "Auto/Manual（true=Auto，false=Manual）")]
    public bool IsAutoMode { get; private set; }

    private volatile string? _carrierId;

    /// <summary>
    /// 当前载具 ID（SV）：读卡成功或 Host 改写后有值；未读或载具已移走为 null。
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

    private volatile LoadPortStatus? _status;

    /// <summary>
    /// 最近一次成功查询的设备状态；尚未查询成功或查询超时时为 null。
    /// </summary>
    public LoadPortStatus? Status
    {
        get => _status;
        protected set => _status = value;
    }
    #endregion

    #region SC 

    [SCEditor("", "LoadPort", "LoadPort 品牌")]
    public string Brand { get; set; } = string.Empty;

    [SCEditor("True", "LoadPort", "是否启用本 LoadPort (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("Serial", "LoadPort", "通讯类型：Serial=串口，Tcp=网口")]
    public CommType CommType { get; set; } = CommType.Serial;

    [SCEditor("COM", "LoadPort", "LoadPort 串口名称（CommType=Serial 时生效）")]
    public string PortName { get; set; } = string.Empty;

    [SCEditor("9600", "LoadPort", "LoadPort 串口波特率（CommType=Serial 时生效）")]
    public int BaudRate { get; set; } = 9600;

    [SCEditor("None", "LoadPort", "LoadPort 串口校验位（CommType=Serial 时生效）")]
    public string Parity { get; set; } = "None";

    [SCEditor("8", "LoadPort", "LoadPort 串口数据位（CommType=Serial 时生效）")]
    public int DataBits { get; set; } = 8;

    [SCEditor("One", "LoadPort", "LoadPort 串口停止位（CommType=Serial 时生效）")]
    public string StopBits { get; set; } = "One";

    [SCEditor("192.168.1.100", "LoadPort", "网口 IP（CommType=Tcp 时生效）")]
    public string Host { get; set; } = "192.168.1.100";

    [SCEditor("4004", "LoadPort", "网口端口（CommType=Tcp 时生效）")]
    public int NetPort { get; set; } = 4004;

    [SCEditor("25", "LoadPort", "花篮槽数")]
    public int SlotCount { get; set; } = 25;

    [SCEditor("1", "LoadPort", "机械手从本 LoadPort 取片用的手臂")]
    public int UseArm { get; set; } = 1;

    [SCEditor("True", "LoadPort", "载具到位后自动读码（False=只由上层/EAP 显式触发）")]
    public bool AutoReadCarrierId { get; set; } = true;

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

    #endregion

    #region Alarm

    [Alarm("LoadPort 初始化超时", AlarmCategory.Timeout,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "LoadPort 初始化未在指定时间内完成",
        Solution = "检查串口连接、LoadPort 硬件状态及供电")]
    public string InitTimeoutAlarm = nameof(InitTimeoutAlarm);

    [Alarm("LoadPort 受控停止", AlarmCategory.ProcessError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "LoadPort 进入受控停止状态",
        Solution = "检查 LoadPort 当前状态并复位")]
    public string ControlledStopAlarm = nameof(ControlledStopAlarm);

    [Alarm("LoadPort 设备报警", AlarmCategory.HardwareError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "LoadPort 设备本身报警",
        Solution = "检查 LoadPort 硬件/通讯状态")]
    public string LoadPortDeviceAlarm = nameof(LoadPortDeviceAlarm);

    #endregion

    #region Event

    [EventAttribut("FOUP 到达", Description = "FOUP 从不在位变为在位")]
    public readonly string FoupArrivedEvent = "FoupArrived";

    [EventAttribut("FOUP 移除", Description = "FOUP 从在位变为不在位")]
    public readonly string FoupRemovedEvent = "FoupRemoved";

    #endregion

    #region Component

    public IRfidReader? RFID => FindChild<IRfidReader>();

    /// <summary>
    /// E84 交接组件；没配（本机型没有 E84）为 null。
    /// </summary>
    public IE84? E84 => FindChild<IE84>();

    #endregion

    #region 载具

    private readonly object _carrierGate = new();
    private volatile CarrierInfo? _carrier;

    public CarrierInfo? Carrier => _carrier;

    private void UpdateCarrier(Func<CarrierInfo, CarrierInfo> change)
    {
        lock (_carrierGate)
        {
            if (_carrier is not { } carrier)
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

    public ILoadPortDriver? Driver { get; private set; }

    /// <summary>
    /// 按 sc.xml 配的 CommType 建传输。
    /// </summary>
    protected ICommunication CreateTransport()
    {
        return CommunicationFactory.Create(CommType, PortName, BaudRate, Parity, DataBits, StopBits, Host, NetPort);
    }

    /// <summary>
    /// 创建品牌驱动（传输 + 品牌帧编解码 + 驱动）；机型模块重写。
    /// </summary>
    protected virtual ILoadPortDriver CreateDriver()
    {
        throw new NotSupportedException($"{GetType().Name} 尚未实现 CreateDriver。");
    }

    /// <summary>
    /// 打开驱动连接并订阅主动事件；由装配在 Start 之前调用。
    /// 装机停用（IsEnable=False）的模块视为打开成功，空转。
    /// </summary>
    public override bool Open()
    {
        if (!IsEnable)
        {
            return true;
        }

        var rfid = RFID;
        if (rfid is not null && !rfid.Open())
        {
            return false;
        }

        var e84 = E84;
        if (e84 is not null && !e84.Open())
        {
            return false;
        }

        // 先在晶圆账上占好槽位，Mapping 一到就能直接落账。
        WaferManager.Current?.RegisterLocation(Name, SlotCount);

        Driver = CreateDriver();
        Driver.OnSpontaneousEvent += OnDriverSpontaneousEvent;  //事件
        return Driver.Open();
    }

    /// <summary>
    /// 关闭 RFID 读头与驱动连接，并结束 EAP 派发线程；与 Open 成对，宿主退出时调用（当前宿主常驻，暂无调用点）。
    /// </summary>
    public void Close()
    {
        RFID?.Close();
        Driver?.Close();
        _eapNotifications.Writer.TryComplete();
    }

    private void OnDriverSpontaneousEvent(LoadPortDeviceEvent evt)
    {
        // 在驱动路由线程回调，只做轻量状态翻转。
        switch (evt.Kind)
        {
            case LoadPortDeviceEventKind.PodPresent:
                IsPodPlaced = true;
                break;

            case LoadPortDeviceEventKind.PodRemoved:
                IsPodPlaced = false;
                break;
        }
    }

    #endregion

    #region 状态发布

    private LoadPortDto? _lastPublishedState;

    /// <summary>
    /// 当前状态快照，状态发布与 GetState 查询共用。
    /// 未连接或停用时查询反馈（在位、门、报警）不可信，置 null；载具 ID 与 Mapping 结果取模块当前值（载具移走时已清空）。
    /// </summary>
    public LoadPortDto CreateStateDto()
    {
        var carrier = Carrier;
        var dto = new LoadPortDto
        {
            Name = Name,
            State = State,
            Mode = Mode,
            IsPodPlaced = IsPodPlaced,
            AutoMode = IsAutoMode,
            CarrierId = CarrierId ?? string.Empty,
            Slots = ToSlotDtos(SlotMap),
            HasCarrier = carrier is not null,
            LotId = carrier?.LotId ?? string.Empty,
            CarrierIdStatus = carrier?.IdStatus ?? CarrierIdStatus.NotRead,
            CarrierSlotMapStatus = carrier?.SlotMapStatus ?? CarrierSlotMapStatus.NotRead,
            CarrierAccessStatus = carrier?.AccessStatus ?? CarrierAccessStatus.NotAccessed,
        };

        var driver = Driver;
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
            dto.IsPodPlaced = status.PodPresent;
            dto.PodPresent = status.PodPresent;
            dto.PodPlaced = status.PodPlaced;
            dto.DoorOpen = status.DoorOpen;
            dto.DoorClosed = status.DoorClosed;
            dto.DeviceAlarm = status.DeviceAlarm;
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

    #region Action（ILoadPort 契约：动作体由机型实现——直接创建操作）

    private (int ExecutingState, int SuccessState) _transition;
    private LoadPortAction _action;

    /// <summary>
    /// 发起一次读码。读码要走好几轮握手（几百毫秒），所以这里只发起、不等结果——
    /// 读完之后 CarrierId 会更新，并回调 EAP 的 CarrierIdRead / CarrierIdReadFailed。
    /// 未挂读头组件、读头没连上或上一次还没读完，返回 false。
    /// </summary>
    public bool ReadCarrierId()
    {
        var reader = RFID;
        return reader is not null && reader.BeginRead();
    }

    /// <summary>
    /// 收读码结果：成功更新 CarrierId 并回调 CarrierIdRead，失败回调 CarrierIdReadFailed。
    /// 在扫描线程上跑（读头的步进机由 base.OnScan 递归带着走，这里紧跟着取结果）。
    /// </summary>
    private void CheckCarrierIdRead()
    {
        if (RFID?.TakeResult() is not { } result)
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
        WaferManager.Current?.SetCarrierIdOn(Name, carrierId);
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
        WaferManager.Current?.SetCarrierIdOn(Name, carrierId);
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
        var ledger = WaferManager.Current;
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
    /// 发起 Load。机型实现：Begin(LoadPortAction.Load, new ...Operation(...))。
    /// </summary>
    public abstract ModuleOperation? Load();

    /// <summary>
    /// 发起 Unload。
    /// </summary>
    public abstract ModuleOperation? Unload();

    /// <summary>
    /// 发起 Home。
    /// </summary>
    public abstract ModuleOperation? Home();

    /// <summary>
    /// 发起 Reset。
    /// </summary>
    public abstract ModuleOperation? Reset();

    /// <summary>
    /// 发起 Abort；Abort 可顶替在途动作。
    /// </summary>
    public abstract ModuleOperation? Abort();

    /// <summary>
    /// 发起 Clamp（夹紧 FOUP），状态表只允许空闲时发起。
    /// </summary>
    public abstract ModuleOperation? Clamp();

    /// <summary>
    /// 发起 Unclamp（松开 FOUP），状态表只允许空闲时发起。
    /// </summary>
    public abstract ModuleOperation? Unclamp();

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

    protected ModuleOperation? Begin(LoadPortAction action, ModuleOperation operation)
    {
        lock (OperationGate)
        {
            if (!IsEnable || Driver is null)
            {
                return null;
            }

            if (!TryGetTransition(State, action.ToString(), out var transition))
            {
                return null;
            }

            if (!Run(operation, replace: action == LoadPortAction.Abort))
            {
                return null;
            }

            _transition = transition;
            _action = action;
            State = transition.ExecutingState;
            return operation;
        }
    }

    /// <summary>
    /// 操作终结：成功落迁移表的成功态，失败/被打断落 Error。
    /// 成功的动作与失败的原因都回调 EAP（在模块锁内只入队，派发在扫描线程锁外进行）。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        State = operation.IsSuccess ? _transition.SuccessState : ModuleState.Error;
        UpdateActionAlarms(operation);

        if (!operation.IsSuccess)
        {
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
                // 门已开，机械手可以取放：E87 访问状态进 IN_ACCESS。
                UpdateCarrier(carrier => carrier with { AccessStatus = CarrierAccessStatus.InAccess });
                EnqueueE87(callback => callback.LoadCompleted(this));
                EnqueueE87(callback => callback.AccessStarted(this));
                break;

            case LoadPortAction.Unload:
                // 门已关，这一轮取放结束。干没干完不由这里判——上层作业调 NoteCarrierComplete 才算完成，
                // 所以只把 InAccess 退回未取放，已经 Complete/Stopped 的保持原样。
                UpdateCarrier(carrier => carrier.AccessStatus == CarrierAccessStatus.InAccess
                    ? carrier with { AccessStatus = CarrierAccessStatus.NotAccessed }
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
    /// EAP 回调积压到这个条数的整数倍时记一次告警。
    /// </summary>
    private const int EapBacklogWarning = 500;

    private readonly Channel<Action> _eapNotifications =
        Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });

    private int _eapDispatchStarted;
    private int _eapPending;
    private bool _lastPodPlaced;

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
    /// 推 E84 一拍，交接进展放进 EAP 派发队列（和 E87 回调同一条，先后不乱）；没配 E84 组件什么都不做。
    /// </summary>
    private void StepE84()
    {
        if (E84 is not { } e84)
        {
            return;
        }

        foreach (var report in e84.Step(CurrentE84Permit(), IsPodPlaced))
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

        return (E84Provider?.GetTransferState(this) ?? LocalTransferState()) switch
        {
            LoadPortTransferState.OutOfService => E84Permit.NotAvailable,
            LoadPortTransferState.ReadyToLoad => E84Permit.ReadyToLoad,
            LoadPortTransferState.ReadyToUnload => E84Permit.ReadyToUnload,
            _ => E84Permit.Blocked,
        };
    }

    /// <summary>
    /// 没接 EAP 时按本地状态判断端口搬运状态（E87 那套由 EAP 维护，这里只给 E84 用）：
    /// 停用、下线、未初始化或出错 → Out Of Service；不在空闲 → 挡住；
    /// 空闲且没载具 → 等送盒；有载具且这一盒已经干完或中断（Complete/Stopped）→ 等取走；其余挡住。
    /// </summary>
    private LoadPortTransferState LocalTransferState()
    {
        if (!IsEnable || Mode != ModuleMode.Online || State == ModuleState.NotInit || State == ModuleState.Error)
        {
            return LoadPortTransferState.OutOfService;
        }

        if (State != ModuleState.Idle)
        {
            return LoadPortTransferState.TransferBlocked;
        }

        if (!IsPodPlaced)
        {
            return LoadPortTransferState.ReadyToLoad;
        }

        return Carrier?.AccessStatus is CarrierAccessStatus.Complete or CarrierAccessStatus.Stopped
            ? LoadPortTransferState.ReadyToUnload
            : LoadPortTransferState.TransferBlocked;
    }

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
    /// 扫描周期：先扫子组件与操作（基类，读头的读码步进机也在里面），
    /// 再判载具在位边沿、推 E84、收读码结果；EAP 回调由专用派发线程发，不占扫描线程。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        CheckCarrierPresence();
        StepE84();
        CheckCarrierIdRead();
        CheckDeviceAlarm();
    }

    /// <summary>
    /// 设备报警跟着状态查询走：报警位亮就报，灭了就恢复。
    /// 查不到（没连上/查询超时，Status 为 null）不算恢复——不知道不等于没事。
    /// </summary>
    private void CheckDeviceAlarm()
    {
        if (Status is not { } status)
        {
            return;
        }

        if (status.DeviceAlarm)
        {
            AlarmComponent.Current?.Raise(this, LoadPortDeviceAlarm);
        }
        else
        {
            AlarmComponent.Current?.Clear(this, LoadPortDeviceAlarm);
        }
    }

    /// <summary>
    /// 动作类报警：失败就报，成功并回到能干活的状态才恢复。
    /// 人为急停顶掉的不报——那是操作员自己按的，不是故障。
    /// </summary>
    private void UpdateActionAlarms(ModuleOperation operation)
    {
        var alarms = AlarmComponent.Current;
        if (alarms is null)
        {
            return;
        }

        if (operation.IsSuccess)
        {
            if (State != ModuleState.NotInit && State != ModuleState.Error)
            {
                alarms.Clear(this, ControlledStopAlarm);
                if (_action == LoadPortAction.Home)
                {
                    alarms.Clear(this, InitTimeoutAlarm);
                }
            }

            return;
        }

        if (operation.Code == ErrorCodes.Aborted)
        {
            return;
        }

        alarms.Raise(this, ControlledStopAlarm);
        if (_action == LoadPortAction.Home && operation.Code == ErrorCodes.Timeout)
        {
            alarms.Raise(this, InitTimeoutAlarm);
        }
    }

    /// <summary>
    /// 在位边沿：放上回调 CarrierArrived；移走先清载具 ID 与 Mapping，再回调 CarrierRemoved。
    /// 在位位由驱动路由线程翻转，这里在扫描线程判边沿，保证回调顺序。
    /// </summary>
    private void CheckCarrierPresence()
    {
        bool placed = IsPodPlaced;
        if (placed == _lastPodPlaced)
        {
            return;
        }

        _lastPodPlaced = placed;
        if (placed)
        {
            lock (_carrierGate)
            {
                _carrier = new CarrierInfo { Location = Name, Capacity = SlotCount };
            }

            EnqueueE87(callback => callback.CarrierArrived(this));
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
        WaferManager.Current?.Clear(Name);
        EnqueueE87(callback => callback.CarrierRemoved(this, carrierId));
    }

    /// <summary>
    /// 入队一条 E87 回调；未挂 EAP 时直接丢弃。任意线程可调。
    /// </summary>
    private void EnqueueE87(Action<IE87Callback> notification)
    {
        if (E87Callback is null)
        {
            return;
        }

        Enqueue(() =>
        {
            var callback = E87Callback;
            if (callback is not null)
            {
                notification(callback);
            }
        });
    }

    /// <summary>
    /// 入队一条 E84 回调；未挂 EAP 时直接丢弃。任意线程可调（E84 信号由 IO 线程翻转）。
    /// </summary>
    protected void EnqueueE84(Action<IE84Callback> notification)
    {
        if (E84Callback is null)
        {
            return;
        }

        Enqueue(() =>
        {
            var callback = E84Callback;
            if (callback is not null)
            {
                notification(callback);
            }
        });
    }

    /// <summary>
    /// 投递一条回调并确保派发线程已起；积压超过水位只记日志，不丢事件（EAP 事件丢了比慢更糟）。
    /// </summary>
    private void Enqueue(Action notification)
    {
        EnsureDispatchRunning();
        if (!_eapNotifications.Writer.TryWrite(notification))
        {
            return;
        }

        int pending = Interlocked.Increment(ref _eapPending);
        if (pending > 0 && pending % EapBacklogWarning == 0)
        {
            LogHelper.Warn(Name, $"EAP 回调积压 {pending} 条，检查 EAP 侧是否卡住");
        }
    }

    /// <summary>
    /// 第一次真正入队时才起派发线程：没接 EAP 的机台不会多出这个线程。
    /// </summary>
    private void EnsureDispatchRunning()
    {
        if (Interlocked.CompareExchange(ref _eapDispatchStarted, 1, 0) == 0)
        {
            _ = Task.Run(DispatchEapLoopAsync);
        }
    }

    /// <summary>
    /// 专用线程按入队顺序派发，不持模块锁也不占扫描线程：EAP 侧发 SECS 阻塞时不会卡住设备轮询。
    /// 单条异常只记日志；Close 后队列关闭、循环自然结束。
    /// </summary>
    private async Task DispatchEapLoopAsync()
    {
        await foreach (var notification in _eapNotifications.Reader.ReadAllAsync())
        {
            Interlocked.Decrement(ref _eapPending);
            try
            {
                notification();
            }
            catch (Exception exception)
            {
                LogHelper.Warn(Name, $"EAP 回调异常: {exception.Message}");
            }
        }
    }

    #endregion
}
