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

    /// <summary>
    /// 状态
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "模块状态码")]
    public override int State { get; protected set; } = ModuleState.NotInit;

    /// <summary>
    /// Auto/Manual
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "Auto/Manual（true=Auto，false=Manual）")]
    public bool IsAutoMode { get; private set; }

    #endregion

    #region SC 

    [SCEditor("True", "LoadPort", "是否启用本 LoadPort (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("25", "LoadPort", "花篮槽数")]
    public override int SlotCount { get; set; } = 25;

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

    #region Component

    public LoadPortDriverComponent? _driver { get; private set; }

    public ICarrierIdReader? _rfid { get; private set; }

    public ICarrier _carrier { get; private set; } = null!;

    public IE84? E84
    {
        get
        {
            var e84 = FindChild<IE84>();
            return e84 is not null && e84.IsEnable ? e84 : null;
        }
    }

    #endregion

    #region 状态信息

    private volatile LoadPortStatus? _status;

    public LoadPortStatus? Status
    {
        get => _status;
        protected set => _status = value;
    }

    #endregion

    protected BaseLoadPortModule()
    {
        RegisterTransitions(LoadPortStateTable.ToModuleTable());
    }

    /// <summary>
    /// 传片环待命态：LoadPort 已装载（Loaded）即可被机械手服务。
    /// </summary>
    protected override int StandbyState => LoadPortState.Loaded;

    #region 组件初始化与驱动连接

    /// <summary>
    /// 组件初始化
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public override bool InitComponent()
    {
        if (!IsEnable)
        {
            return true;
        }

        #region 载具

        var carrier = FindChild<ICarrier>();
        if (carrier is null)
        {
            throw new InvalidOperationException($"LoadPort {Name} 的 sc.xml 节点下没配 _carrier 子组件（实现 ICarrier 的组件，平台默认 Type=xyz.Modules.CarrierComponent）。");
        }

        // 读头交给载具读码用；没配读头为 null（这个端口不读码，ID 由 Host 给）。
        _rfid = FindChild<ICarrierIdReader>();
        carrier.Attach(this, _rfid, EnqueueE87);
        _carrier = carrier;

        #endregion

        #region LoadPort 驱动

        var driver = FindChild<LoadPortDriverComponent>();
        if (driver is not null)
        {
            // 先摘后挂：初始化可重入，保证只挂一份；先挂再连，连上就可能有放上 / 拿走的主动上报。
            driver.DeviceEvent -= OnDeviceEvent;
            driver.DeviceEvent += OnDeviceEvent;
            _driver = driver;
        }
        else
        {
            LogHelper.Error(Name, "sc.xml 未挂品牌驱动组件（_driver 子节点），无法打开");
        }

        #endregion

        ///晶圆账注册
        WaferManagerComponent.Current?.RegisterLoadPort(Name, SlotCount);

        bool childrenInitialized = base.InitComponent();
        return driver is not null && childrenInitialized;
    }

    /// <summary>
    /// 关闭 _rfid 和loadport
    /// </summary>
    public void Close()
    {
        _rfid?.Close();
        _driver?.Close();
    }

    /// <summary>
    /// 在位和移除事件回调
    /// </summary>
    /// <param name="evt"></param>
    private void OnDeviceEvent(LoadPortDeviceEvent evt)
    {
        
        switch (evt.Kind)
        {
            case LoadPortDeviceEventKind.PodPresent:
                SetDeviceReportedPlaced(true);
                break;

            case LoadPortDeviceEventKind.PodRemoved:
                SetDeviceReportedPlaced(false);
                break;
        }
    }

    #endregion

    #region 设备上报的放上 / 拿走（转给载具）

    /// <summary>
    /// 记录到位
    /// </summary>
    /// <param name="placed"></param>
    protected void SetDeviceReportedPlaced(bool placed)
    {
        _carrier.SetDeviceReportedPlaced(placed);
    }

    #endregion

    #region 设备状态查询（每拍一条：Query 判在位、设备报警、界面的设备反馈都靠它）

    private LoadPortCommand? _loadPortQuery;
    private readonly Stopwatch _statusQueryWatch = new();

    private bool _isStatusQueryLate;

    private void LoopQueryStatus()
    {
        if (!IsEnable || _driver is null)
        {
            return;
        }

        if (!_driver.IsConnected)
        {
            Status = null;
            _loadPortQuery = null;
            return;
        }

        var query = _loadPortQuery;
        if (query is null)
        {
            query = _driver.QueryStatus();
            if (query is not null)
            {
                _loadPortQuery = query;
                _statusQueryWatch.Restart();
            }

            return;
        }

        if (query.IsCompleted)
        {
            _loadPortQuery = null;
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

        _driver.Abandon(query, "Timeout");
        _loadPortQuery = null;
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
        var carrier = _carrier;
        var info = carrier.Info;
        var dto = new LoadPortDto
        {
            Name = Name,
            State = State,
            Mode = Mode,
            IsCarrierArrived = carrier.IsArrived,
            AutoMode = IsAutoMode,
            CarrierId = carrier.CarrierId ?? string.Empty,
            SlotCount = SlotCount,
            Slots = ToSlotDtos(carrier.SlotMap),
            LedgerSlots = WaferLedgerSnapshot.SlotsOf(Name),
            HasCarrier = info is not null,
            LotId = info?.LotId ?? string.Empty,
            CarrierIdStatus = info?.IdStatus ?? CarrierIdStatus.NotRead,
            CarrierSlotMapStatus = info?.SlotMapStatus ?? CarrierSlotMapStatus.NotRead,
            CarrierAccessStatus = info?.AccessStatus ?? CarrierAccessStatus.NotAccessed,
        };

        if (_driver is not null)
        {
            dto.IsConnected = _driver.IsConnected;
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

    /// <summary>这趟动作发起前模块在什么状态：复位、中止做完按它判门有没有在动（见 SetStateByDoor）。</summary>
    private int _actionFrom;

    /// <summary>
    /// Mapping 数据到达时调用：交给载具更新槽图、整篮落晶圆账、报 EAP 的 SlotMapRead，空列表忽略。
    /// 平台默认的 Load 成功后调它；机型自己重写 Load 的话，拿到 Mapping 结果也调它。
    /// </summary>
    protected void UpdateSlotMap(IReadOnlyList<SlotState> slotMap)
    {
        _carrier.NoteMapped(slotMap);
    }

    /// <summary>
    /// 发起 Load（开门 + Mapping）：平台默认发驱动的 Load 指令，成功后把 Mapping 结果落下来（UpdateSlotMap）。
    /// Load 联锁（LoadInterlock，默认要载具到了）不让发时返回 null。机型的 Load 要多做别的步骤就重写。
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
    /// 模块初始化（动硬件，重写 BaseModule 的 InitModule）：回原点——Home 就是 LoadPort 的初始化，
    /// 超时报的也是"初始化超时"。人或调度才调，开机不调。返回 Home 操作，调用方等它做完；状态不允许时为 null。
    /// </summary>
    public override ModuleOperation? InitModule()
    {
        return Home();
    }

    /// <summary>
    /// 复位（重写组件基类的 Reset）：先清报警、复位子组件（E84、_rfid），再发设备复位清错。
    /// 返回设备复位操作，调用方等它做完；状态不允许时为 null，报警照样已经清了。
    /// 复位只清错：没初始化、出过错的复位完是 NotInit，要再 Home；Load 好了的复位完按门位落 Loaded / Idle，查不到是 NotInit。
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
    /// 返回设备中止操作；状态不允许时为 null。中止只停：空闲的还是 Idle，出错的还是 Error；门开着没在动的（Loaded、正被机械手取放）
    /// 按门位落 Loaded / Idle；其余（没初始化、打断了 Load / Unload / Home / 夹紧松开、查不到门位）落 NotInit，要人 Home。
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
    /// 装机停用、或驱动还没建起来（装配里组件初始化没做成）都不发动作。
    /// </summary>
    protected override bool CanBeginAction => IsEnable && _driver is not null;

    /// <summary>
    /// Load 联锁：现在能不能发 Load。平台默认看载具到了没有（_carrier.IsArrived）——没载具设备只会回错，白白落 Error、报受控停止。
    /// 机型有别的条件（光幕、机械手缩回……）就重写，先调 base。在 Begin 里查，手动、E87 自动 Load、机型重写的 Load 都过这一关。
    /// 在模块锁里调：里面只读状态，别等待、别去拿别的模块的锁。
    /// </summary>
    protected virtual bool LoadInterlock()
    {
        return _carrier.IsArrived;
    }

    /// <summary>
    /// 发起动作：发起这件事走基类，这儿多记两笔——这趟发的是什么动作（终结时按它回调 EAP）、发之前模块在什么状态（复位、中止做完按它落状态）。
    /// 记在同一把锁里：否则扫描线程可能在记上之前就把操作终结了，回调就发错。
    /// Load 先过联锁（LoadInterlock），不让发返回 null。
    /// </summary>
    protected ModuleOperation? Begin(LoadPortAction action, ModuleOperation operation)
    {
        lock (OperationGate)
        {
            if (action == LoadPortAction.Load && !LoadInterlock())
            {
                return null;
            }

            int from = State;
            var started = base.Begin(action, operation);
            if (started is not null)
            {
                _action = action;
                _actionFrom = from;
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

            // 取放途中出错：这个载具算没干完，记中断（还没开始取放的载具不动）。
            _carrier.NoteFault();
            EnqueueE87(callback => callback.PortError(this, reason));
            return;
        }

        switch (_action)
        {
            case LoadPortAction.Load:
                // 门已开、槽图读了，就算开始取放（照老 CTC）。
                EnqueueE87(callback => callback.LoadCompleted(this));
                _carrier.NoteLoaded();
                break;

            case LoadPortAction.Unload:
                // 门已关，取放结束。干完没干完不由这里判——上层作业调 _carrier.NoteComplete 才算完成；
                // 取放过、没判完成就 Unload 了，载具算中断（E87 CARRIER STOPPED），已经 Complete/Stopped 的保持原样。
                _carrier.NoteUnloaded();
                EnqueueE87(callback => callback.UnloadCompleted(this));
                break;

            case LoadPortAction.Reset:
            case LoadPortAction.Abort:
                SetStateByDoor();
                break;
        }
    }

    /// <summary>
    /// 复位只清错、中止只停，门不会因为它们动。动作前门开着没在动（Loaded，或正被机械手取放）的，状态表落的是最保守的 NotInit，
    /// 这里按状态查询的门位改：门开着、载具还在 → Loaded（机械手接着能进，不用再 Load 一遍——重新 Mapping 整篮重建账，片换了标识，
    /// Job 里这一盒剩下的片都对不上了；机械手取片失败卡在取放中，人确认后点中止就走这条回 Loaded）；门关着 → Idle；
    /// 查不到、门停在半路 → 保持 NotInit，要人 Home。Idle 一律当"门关好、没 Load"用，门不确定不能落 Idle。
    /// 打断的是 Load / Unload / Home 这种门在动的，不归这里管，状态表直接落 NotInit。
    /// </summary>
    private void SetStateByDoor()
    {
        if (State != ModuleState.NotInit || !IsLoadedState(_actionFrom))
        {
            return;
        }

        var status = Status;
        if (status is null)
        {
            return;
        }

        if (status.IsDoorOpen && _carrier.IsArrived)
        {
            State = LoadPortState.Loaded;
            return;
        }

        if (status.IsDoorClosed)
        {
            State = ModuleState.Idle;
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

        foreach (var report in e84.Step(CurrentE84Permit(), _carrier.IsArrived))
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

            var carrier = _carrier;
            if (!carrier.IsArrived)
            {
                return LoadPortTransferState.ReadyToLoad;
            }

            return carrier.Info?.AccessStatus is CarrierAccessStatus.Complete or CarrierAccessStatus.Stopped
                ? LoadPortTransferState.ReadyToUnload
                : LoadPortTransferState.TransferBlocked;
        }
    }

    /// <summary>载具 Load 好了：Loaded，或者正被机械手服务（交互环的几个状态）。</summary>
    public bool IsLoaded => IsLoadedState(State);

    /// <summary>这个状态码算不算 Load 好了（门开着、没在动）：Loaded，或者交互环的几个状态。</summary>
    private static bool IsLoadedState(int state)
    {
        return state == LoadPortState.Loaded
            || (state >= LoadPortState.PreTransfer && state <= LoadPortState.TransferComplete);
    }

    /// <summary>
    /// 载具可以取放片：模块已启用、载具已到位且 Load 完成（门开着，端口的事），载具这边也认可了（_carrier.IsAccepted：
    /// 接了 EAP 的槽图要等 Host 认定，没接 EAP 没人核对，Load 好就算）；正被机械手服务时仍然可用。
    /// </summary>
    public bool IsCarrierReady
    {
        get
        {
            var carrier = _carrier;
            return IsEnabled && carrier.IsArrived && IsLoaded && carrier.IsAccepted;
        }
    }

    /// <summary>端口空闲（Idle）：不在做动作、不在被机械手服务。</summary>
    public bool IsIdle => State == LoadPortState.Idle;

    #endregion

    /// <summary>
    /// 扫描周期：先扫子组件与操作（基类，读头的读码步进机、驱动的断线重连、载具收读码结果也在里面），
    /// 再查设备状态、把在位的两位传感器喂给载具、推 E84、查设备报警，最后有变化就推给界面；
    /// EAP 上报放进 EAP 的派发组件发，不占扫描线程。机型重写时先调 base.OnScan()。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        LoopQueryStatus();
        _carrier.Sense(Status?.IsPresent, Status?.IsPlaced);
        StepE84();
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
    /// 一条 E87 上报放进 EAP 的派发组件；未挂 EAP 时直接丢弃。任意线程可调。
    /// 端口自己的上报用它，载具那一半（到达、拿走、读码、槽图、取放开始 / 结束、干完）由载具经挂上时交给它的入队口发，走的是同一条。
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
