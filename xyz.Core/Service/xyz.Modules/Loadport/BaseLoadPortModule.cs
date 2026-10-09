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

    /// <summary>
    /// 机械手能不能进站：在待命态（Loaded，没有别的机械手正在取放），并且载具在、载具这边认可了（接了 EAP 要 Host 认定槽图）。
    /// 调度派机械手前查它；手动传片不经过 Job，也靠 PrepareTransfer 里的同一关挡住。
    /// 不用 IsCarrierReady：那个在机械手正在取放时也是 true，是给 Job 判断"这盒能不能用"的。
    /// </summary>
    public override bool CanPrepare => base.CanPrepare && _carrier.IsArrived && _carrier.IsAccepted;

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
        UpdateActionAlarms(operation);

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

    public bool IsCarrierReady => IsEnabled && _carrier.IsArrived && IsLoaded && _carrier.IsAccepted;

    /// <summary>
    /// 空闲，也就是Unload状态
    /// </summary>
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
