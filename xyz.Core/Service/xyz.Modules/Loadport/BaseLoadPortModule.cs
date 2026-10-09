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

    // 动作超时：一个动作一条，跟 EC 里各动作的超时一一对应；Home 就是初始化
    [Alarm("LoadPort Load 超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "Load（开门 + Mapping）没在 EC LoadTimeout 内做完", Solution = "检查门、Mapping 传感器和 FOUP 有没有放好，看 LoadPort 有没有报错；处理好后复位，再 Home")]
    public string LoadTimeoutAlarm = nameof(LoadTimeoutAlarm);

    [Alarm("LoadPort Unload 超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "Unload（关门）没在 EC UnloadTimeout 内做完", Solution = "检查门有没有被挡、FOUP 有没有放好；处理好后复位，再 Home")]
    public string UnloadTimeoutAlarm = nameof(UnloadTimeoutAlarm);

    [Alarm("LoadPort 初始化超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "Home（初始化回原点）没在 EC HomeTimeout 内做完", Solution = "检查串口连接、LoadPort 硬件状态及供电")]
    public string InitTimeoutAlarm = nameof(InitTimeoutAlarm);

    [Alarm("LoadPort 夹紧超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "夹紧 FOUP 没在 EC ClampTimeout 内做完", Solution = "检查 FOUP 有没有放到位、夹爪有没有卡住；处理好后复位，再 Home")]
    public string ClampTimeoutAlarm = nameof(ClampTimeoutAlarm);

    [Alarm("LoadPort 松开超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "松开 FOUP 没在 EC UnclampTimeout 内做完", Solution = "检查夹爪有没有卡住；处理好后复位，再 Home")]
    public string UnclampTimeoutAlarm = nameof(UnclampTimeoutAlarm);

    [Alarm("LoadPort 复位超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "复位（清设备报错）没在 EC ResetTimeout 内做完", Solution = "检查串口 / 网口连接和 LoadPort 供电，再复位")]
    public string ResetTimeoutAlarm = nameof(ResetTimeoutAlarm);

    [Alarm("LoadPort 中止超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "中止（急停）没在 EC AbortTimeout 内做完", Solution = "检查串口 / 网口连接和 LoadPort 状态；处理好后复位，再 Home")]
    public string AbortTimeoutAlarm = nameof(AbortTimeoutAlarm);

    // 不是超时的失败：设备回了错、指令发不出去
    [Alarm("LoadPort 动作失败", AlarmCategory.ProcessError, AlarmLevel = AlarmLevel.Alarm1, Description = "Load、Unload、Home、夹紧、松开等动作被设备回错或指令发不出去，LoadPort 停在错误状态", Solution = "看报警前后的日志找失败原因，处理好后复位，再 Home")]
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

    /// <summary>
    /// 
    /// </summary>
    public override bool CanPrepare => base.CanPrepare && _carrier.IsArrived && _carrier.IsAccepted&& StandbyState== LoadPortState.Loaded;

    /// <summary>
    /// 机械手进站（准备一）：载具不在、或者 Host 还没认定槽图就不让进，返回 null（搬运那边按站点忙接着等，等到超时判负）。
    /// 在不在待命态由基类在锁里判，两台机械手抢同一个口只有一台进得去。
    /// </summary>
    public override ModuleOperation? PrepareTransfer()
    {
        if (!_carrier.IsArrived || !_carrier.IsAccepted)
        {
            return null;
        }

        return base.PrepareTransfer();
    }

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

    #region 设备状态查询

    private LoadPortCommand? _loadPortQuery;
    private readonly Stopwatch _statusQueryWatch = new();

    private bool _isStatusQueryLate;

    private void LoopQueryStatus()
    {
        if (!IsEnable || _driver is null)
        {
            return;
        }

        #region 没连上：状态清空

        if (!_driver.IsConnected)
        {
            Status = null;
            _loadPortQuery = null;
            return;
        }

        #endregion

        #region 发查询

        // 手上没有在途的查询就发一条；发不出去（驱动正忙）是 null，下一拍再发。
        if (_loadPortQuery is null)
        {
            _loadPortQuery = _driver.QueryStatus();
            _statusQueryWatch.Restart();
            return;
        }

        #endregion

        #region 收结果

        if (_loadPortQuery.IsCompleted)
        {
            var response = _loadPortQuery.Response;
            _loadPortQuery = null;
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

        #endregion

        #region 超时：作废这一条，下一拍重发

        int timeout = QueryDataTimeOut;
        if (_statusQueryWatch.ElapsedMilliseconds < timeout)
        {
            return;
        }

        _driver.Abandon(_loadPortQuery, "Timeout");
        _loadPortQuery = null;
        Status = null;
        if (!_isStatusQueryLate)
        {
            _isStatusQueryLate = true;
            LogHelper.Warn(Name, $"状态查询超时（{timeout}ms）：这一条作废、接着查，查到之前在位保持原判断");
        }

        #endregion
    }

    #endregion

    #region 状态发布

    private LoadPortDto? _lastPublishedState;

    public LoadPortDto CreateStateDto()
    {
        var info = _carrier.Info;
        var dto = new LoadPortDto
        {
            Name = Name,
            State = State,
            Mode = Mode,
            IsCarrierArrived = _carrier.IsArrived,
            AutoMode = IsAutoMode,
            CarrierId = _carrier.CarrierId ?? string.Empty,
            SlotCount = SlotCount,
            Slots = ToSlotDtos(_carrier.SlotMap),
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
                State = ToSlotState(slotMap[index]),
            });
        }

        return slots;
    }

    /// <summary>
    /// 驱动的槽位状态换成契约的；认不出的算 Undefined。
    /// </summary>
    private static LoadPortSlotState ToSlotState(SlotState state)
    {
        switch (state)
        {
            case SlotState.Empty:
                return LoadPortSlotState.Empty;

            case SlotState.NotEmpty:
                return LoadPortSlotState.NotEmpty;

            case SlotState.CorrectlyOccupied:
                return LoadPortSlotState.CorrectlyOccupied;

            case SlotState.DoubleSlotted:
                return LoadPortSlotState.DoubleSlotted;

            case SlotState.CrossSlotted:
                return LoadPortSlotState.CrossSlotted;

            default:
                return LoadPortSlotState.Undefined;
        }
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

    #region ILoadPort 契约

    private LoadPortAction _action;

    /// <summary>
    /// 动作之前的状态
    /// </summary>
    private int _actionFrom;

    /// <summary>
    /// Mapping 数据到达时调用,给载具对象
    /// </summary>
    /// <param name="slotMap"></param>
    protected void UpdateSlotMap(IReadOnlyList<SlotState> slotMap)
    {
        _carrier.UpdateSlotMap(slotMap);
    }

    /// <summary>
    /// 模块初始化
    /// </summary>
    /// <returns></returns>
    public override ModuleOperation? InitModule()
    {
        return Home();
    }

    public virtual ModuleOperation? Home()
    {
        return Begin(LoadPortAction.Home, new LoadPortCommandOperation("Home", () => _driver?.Home(), () => HomeTimeout));
    }

    public virtual ModuleOperation? Load()
    {
        return Begin(LoadPortAction.Load, new LoadPortCommandOperation("Load", () => _driver?.Load(), () => LoadTimeout,
            response => UpdateSlotMap(response.SlotMap)));
    }

    public virtual ModuleOperation? Unload()
    {
        return Begin(LoadPortAction.Unload, new LoadPortCommandOperation("Unload", () => _driver?.Unload(), () => UnloadTimeout));
    }

    public virtual ModuleOperation? Clamp()
    {
        return Begin(LoadPortAction.Clamp, new LoadPortCommandOperation("Clamp", () => _driver?.Clamp(), () => ClampTimeout));
    }

    public virtual ModuleOperation? Unclamp()
    {
        return Begin(LoadPortAction.Unclamp, new LoadPortCommandOperation("Unclamp", () => _driver?.Unclamp(), () => UnclampTimeout));
    }

    public override ModuleOperation? Reset()
    {
        base.Reset();
        return ResetDevice();
    }
    protected virtual ModuleOperation? ResetDevice()
    {
        return Begin(LoadPortAction.Reset, new LoadPortCommandOperation("Reset", () => _driver?.ResetDrive(), () => ResetTimeout));
    }

    public override ModuleOperation? Abort()
    {
        base.Abort();
        return AbortDevice();
    }

    protected virtual ModuleOperation? AbortDevice()
    {
        return Begin(LoadPortAction.Abort, new LoadPortCommandOperation("Abort", () => _driver?.Stop(), () => AbortTimeout));
    }

    /// <summary>
    ///  切 Auto/Manual 并且和E84有关系
    /// </summary>
    /// <param name="autoMode"></param>
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
    /// 执行动作之前的基础前提
    /// </summary>
    protected override bool CanBeginAction => IsEnable && _driver is not null;

    protected ModuleOperation? Begin(LoadPortAction action, ModuleOperation operation)
    {
        lock (OperationGate)
        {
            // 各动作自己的联锁（见 Interlock 区），不让发返回 null；复位、中止不设联锁。
            switch (action)
            {
                case LoadPortAction.Load:
                    if (!LoadInterlock())
                    {
                        return null;
                    }

                    break;

                case LoadPortAction.Unload:
                    if (!UnloadInterlock())
                    {
                        return null;
                    }

                    break;

                case LoadPortAction.Home:
                    if (!HomeInterlock())
                    {
                        return null;
                    }

                    break;

                case LoadPortAction.Clamp:
                    if (!ClampInterlock())
                    {
                        return null;
                    }

                    break;

                case LoadPortAction.Unclamp:
                    if (!UnclampInterlock())
                    {
                        return null;
                    }

                    break;
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

    #region Interlock

    // 各动作发起前的联锁，在 Begin 里查：手动、自动、机型重写的动作都过这一关。
    // 机型有别的条件（光幕、机械手缩回……）就重写，先调 base。在模块锁里调：只读状态，别等待、别去拿别的模块的锁。

    /// <summary>
    /// Load 联锁：平台默认要载具到了——没载具设备只会回错，白白落 Error、报受控停止
    /// </summary>
    protected virtual bool LoadInterlock()
    {
        return _carrier.IsArrived;
    }

    /// <summary>
    /// Unload 联锁：平台默认不拦
    /// </summary>
    protected virtual bool UnloadInterlock()
    {
        return true;
    }

    /// <summary>
    /// Home 联锁：平台默认不拦
    /// </summary>
    protected virtual bool HomeInterlock()
    {
        return true;
    }

    /// <summary>
    /// Clamp 联锁：平台默认不拦
    /// </summary>
    protected virtual bool ClampInterlock()
    {
        return true;
    }

    /// <summary>
    /// Unclamp 联锁：平台默认不拦
    /// </summary>
    protected virtual bool UnclampInterlock()
    {
        return true;
    }

    #endregion

    /// <summary>
    /// 动作完成之后的钩子
    /// </summary>
    /// <param name="operation"></param>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        RaiseActionFailedAlarm(_action, operation);

        //动作失败
        if (!operation.IsSuccess)
        {
            _driver?.AbandonAll("Abandoned");  //作废指令
            string reason = operation.Reason;

            // 取放途中出错：这个载具算没干完，记中断（还没开始取放的载具不动）。
            _carrier.MarkAccessStopped();
            EnqueueE87(callback => callback.PortError(this, reason));
            return;
        }

        switch (_action)
        {
            case LoadPortAction.Load:           
                EnqueueE87(callback => callback.LoadCompleted(this));
                _carrier.StartAccess(); //告诉EAP这个是可以  开始取放的
                break;

            case LoadPortAction.Unload:
               
                _carrier.EndAccess();  //结束取放
                EnqueueE87(callback => callback.UnloadCompleted(this));
                break;

            case LoadPortAction.Reset:
            case LoadPortAction.Abort:
                SetStateByDoor();
                break;
        }
    }

    /// <summary>
    /// 只是清楚错误
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

    public IE87Callback? E87Callback { get; set; }

    public IE84Callback? E84Callback { get; set; }

    public IE84Provider? E84Provider { get; set; }

    #region E84

    private void StepE84()
    {
        var e84 = E84;
        if (e84 is null)
        {
            return;
        }

        // 端口只给事实，能不能交接由 E84 自己判。没接 EAP 用端口自己的 Auto/Manual 和搬运状态，接了 EAP 以 EAP 的为准。
        bool auto = IsAutoMode;
        var transferState = LocalTransferState;
        if (E84Provider is not null)
        {
            auto = E84Provider.IsAutoAccessMode(this);
            transferState = E84Provider.GetTransferState(this);
        }

        foreach (var report in e84.Step(auto, transferState, _carrier.IsArrived))
        {
            EnqueueE84(callback => report.DispatchTo(callback, this));
        }
    }

    /// <summary>
    /// 给E84使用
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

            if (!_carrier.IsArrived)
            {
                return LoadPortTransferState.ReadyToLoad;
            }

            return _carrier.Info?.AccessStatus is CarrierAccessStatus.Complete or CarrierAccessStatus.Stopped
                ? LoadPortTransferState.ReadyToUnload
                : LoadPortTransferState.TransferBlocked;
        }
    }

    public bool IsLoaded => IsLoadedState(State);

    private static bool IsLoadedState(int state)
    {
        return state == LoadPortState.Loaded|| (state >= LoadPortState.PreTransfer && state <= LoadPortState.TransferComplete);
    }

    /// <summary>
    /// 载具可以分给 Job（定片、选回片口、开始 CJ）：启用、载具在、门开着（正被机械手取放也算）、Host 认可了槽图。
    /// 只管账面上排活，不管机械手现在能不能进站——那个看 CanPrepare。
    /// </summary>
    public bool CanAssignCarrierToJob => IsEnabled && _carrier.IsArrived && IsLoaded && _carrier.IsAccepted;

    /// <summary>
    /// 空闲，也就是Unload状态
    /// </summary>
    public bool IsIdle => State == LoadPortState.Idle;

    #endregion

    /// <summary>
    /// 扫描周期
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        LoopQueryStatus(); //Loadport 数据轮训
        _carrier.Sense(Status?.IsPresent, Status?.IsPlaced); //盒子到哦没有
        StepE84(); //E84的推进
        CheckDeviceAlarm();  //检查报警
        PublishState();  //推送状态
    }

    /// <summary>
    /// 设备报警跟着状态查询走
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
    /// 动作做完以后报警
    /// </summary>
    /// <param name="action"></param>
    /// <param name="operation"></param>
    protected virtual void RaiseActionFailedAlarm(LoadPortAction action, ModuleOperation operation)
    {
       
        if (operation.IsSuccess || operation.Code == ErrorCodes.Aborted)
        {
            return;
        }

        // 不是超时：报动作失败，端口停在 Error，等人处理、复位
        if (operation.Code != ErrorCodes.Timeout)
        {
            RaiseAlarm(ControlledStopAlarm);
            return;
        }

        // 超时：哪个动作超时就报哪一条
        switch (action)
        {
            case LoadPortAction.Load:
                RaiseAlarm(LoadTimeoutAlarm);
                break;

            case LoadPortAction.Unload:
                RaiseAlarm(UnloadTimeoutAlarm);
                break;

            case LoadPortAction.Home:
                RaiseAlarm(InitTimeoutAlarm);
                break;

            case LoadPortAction.Clamp:
                RaiseAlarm(ClampTimeoutAlarm);
                break;

            case LoadPortAction.Unclamp:
                RaiseAlarm(UnclampTimeoutAlarm);
                break;

            case LoadPortAction.Reset:
                RaiseAlarm(ResetTimeoutAlarm);
                break;

            case LoadPortAction.Abort:
                RaiseAlarm(AbortTimeoutAlarm);
                break;

            default:
                RaiseAlarm(ControlledStopAlarm);
                break;
        }
    }

    /// <summary>
    /// E87 上报放进 EAP 的派发组件
    /// </summary>
    /// <param name="notification"></param>
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
    /// E84 上报放进 EAP 的派发组件
    /// </summary>
    /// <param name="notification"></param>
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
