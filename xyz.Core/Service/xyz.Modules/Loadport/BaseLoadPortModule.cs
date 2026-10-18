using System.Diagnostics;
using System.Globalization;
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

    [SCEditor("False", "LoadPort",
        "自动跑货的 Unload（Job 干完自动卸、Host 放行或取消）要不要再扫一遍图跟晶圆账对：True = 关好门后主动扫一遍（FCD 发 CLDMP）核对，对不上 Unload 判失败、报警；False = 关门就完，不扫、不对账。" +
        "Mapping 是 LoadPort 硬件自带的，这个开关只管自动跑货用不用它；手动卸载不受影响，一律不扫图、不对账")]
    public bool AutoRunMapOnUnload { get; set; }

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "3000", description: "设备状态查询超时")]
    public int QueryDataTimeOut
    {
        get { return GetEcInt(nameof(QueryDataTimeOut)); }
        set { SetEcInt(nameof(QueryDataTimeOut), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "1000", description: "设备状态查询周期")]
    public int QueryStatusIntervalMs
    {
        get { return GetEcInt(nameof(QueryStatusIntervalMs)); }
        set { SetEcInt(nameof(QueryStatusIntervalMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "30000", description: "Load 动作超时（开门+Mapping）")]
    public int LoadTimeout
    {
        get { return GetEcInt(nameof(LoadTimeout)); }
        set { SetEcInt(nameof(LoadTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "30000", description: "Unload 动作超时（关门；自动跑货带图对账时，关门后的扫图另按它算一次）")]
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

    [VariableMark(VariableType.EC, ValueFormat.Bool, @default: "True",
        description: "Job 做完（载具干完）自动 Unload，接不接 EAP 都生效；False = 等操作员点 Unload 或 Host CarrierRelease")]
    public bool AutoUnload
    {
        get { return bool.TryParse(GetEcString(nameof(AutoUnload)), out var autoUnload) && autoUnload; }
        set { SetEc(nameof(AutoUnload), value.ToString()); }
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

    [Alarm("LoadPort Unload 超时", AlarmCategory.Timeout, AlarmLevel = AlarmLevel.Alarm1, Description = "Unload（关门，或自动跑货对账时关门后的扫图）没在 EC UnloadTimeout 内做完", Solution = "检查门有没有被挡、FOUP 有没有放好；处理好后复位，再 Home")]
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

    // 设备做完了、但 Mapping 结果不能用：槽数对不上，有交叉片、叠片、认不出的槽，或者 Unload 时扫到的跟账对不上，都报这一条；
    // EC AutoUnload 关着时载具干完、Load 时的槽图跟晶圆账对不上也报这一条（只报警）
    [Alarm("LoadPort Mapping 异常", AlarmCategory.ProcessError, AlarmLevel = AlarmLevel.Alarm1, Description = "Load 回来的、或 Unload 关好门后扫的 Mapping 不能用：槽数跟 sc.xml 的 SlotCount 对不上，有交叉片、叠片、认不出的槽，或者 Unload 时扫到的片跟晶圆账对不上；动作判失败，LoadPort 停在错误状态。另外 EC AutoUnload 关着时载具干完、Load 时的槽图跟晶圆账对不上也报这一条，这种只报警，端口不停", Solution = "看报警前后的日志：槽数不对就核对 LoadPort 设备的槽数设置和 sc.xml 的 SlotCount；交叉片、叠片就复位、Home 关门，把盒子拿下来理好片再放上 Load；Unload 对账不符就按实物在账单调整页改账，再复位、Home；干完对账不符就按日志里的槽号查实物，在账单调整页改账")]
    public string SlotMapAlarm = nameof(SlotMapAlarm);

    #endregion

    #region Component

    public ILoadPortDriverComponent? _driver { get; private set; }

    public ICarrierIdReader? _rfid { get; private set; }

    public ICarrier _carrier { get; private set; } = null!;

    /// <summary>
    /// 晶圆账：组件初始化时取一次；没配晶圆账、或端口停用（不登记账）为 null。
    /// </summary>
    private IWaferManagerComponent? _waferManager;

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
    /// 开始搬运准备：传片环在 Loaded、载具到位且被认可。
    /// 基类 PrepareTransfer 在占用传片环的同一把锁内重查这里，查询本身不占用端口。
    /// </summary>
    public override HandleResult? CheckPrepareTransfer()
    {
        var stateError = base.CheckPrepareTransfer();
        if (stateError is not null)
        {
            return stateError;
        }

        if (!_carrier.IsArrived)
        {
            return HandleResult.Fail(ErrorCodes.CarrierNotArrived, Name);
        }

        return CheckCarrierAcceptance();
    }

    #region 组件初始化与驱动连接

    /// <summary>
    /// 组件初始化
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public override bool InitComponent()
    {
        #region 载具

        // 停用的端口也要挂上载具，所以放在判 IsEnable 前面：停用的口照样扫描、推状态，Job 按载具号找端口也会挨个查它的载具，
        // 没挂就是空引用（扫描每拍抛、状态推不出去，有一个口停用就建不了 Job）。
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

        // 停用的端口：不连驱动、不登记晶圆账
        if (!IsEnable)
        {
            return true;
        }

        #region LoadPort 驱动

        var driver = FindChild<ILoadPortDriverComponent>();
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
        _waferManager = IocHelper.GetService<IWaferManagerComponent>();
        _waferManager?.RegisterLoadPort(Name, SlotCount);

        bool childrenInitialized = base.InitComponent();
        return driver is not null && childrenInitialized;
    }

    // 下面几个单例开机 Bind 时取；没装是 null
    private IJobManager? _jobs;
    private ITransferManager? _transfers;
    private IEapNotifierComponent? _notifier;

    public override void Bind()
    {
        _jobs = IocHelper.GetService<IJobManager>();
        _transfers = IocHelper.GetService<ITransferManager>();
        _notifier = IocHelper.GetService<IEapNotifierComponent>();
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

        // 手上没有在途的查询、离上一条发出去满一个周期（EC QueryStatusIntervalMs）就发一条；发不出去（驱动正忙）是 null，下一拍再发。
        if (_loadPortQuery is null)
        {
            if (_statusQueryWatch.IsRunning && _statusQueryWatch.ElapsedMilliseconds < QueryStatusIntervalMs)
            {
                return;
            }

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
            AutoMode = IsAutoMode,
            CarrierId = _carrier.CarrierId ?? string.Empty,
            SlotCount = SlotCount,
            Slots = ToSlotDtos(_carrier.SlotMap),
            LedgerSlots = WaferLedgerSnapshot.SlotsOf(_waferManager, Name),
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
    /// 模块初始化 = Home；被拒记一条带原因的日志，返回 null。
    /// </summary>
    public override ModuleOperation? InitModule()
    {
        var started = Home();
        if (!started.IsSuccess)
        {
            LogHelper.Warn(Name, $"初始化（Home）被拒：{started.ErrorMessage} [{string.Join(", ", started.Args)}]");
        }

        return started.Result;
    }

    #region 动作入口：状态 → 资源 → 互锁

    /// <summary>
    /// 状态 → 资源 → 互锁 +原因
    /// </summary>
    /// <param name="action"></param>
    /// <param name="operation"></param>
    /// <returns></returns>
    private HandleResult<ModuleOperation> Started(LoadPortAction action, ModuleOperation operation)
    {
        var started = Begin(action, operation);
        if (started is null)
        {
            return HandleResult<ModuleOperation>.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
        }

        return HandleResult<ModuleOperation>.Success(started);
    }

    #region Load

    /// <summary>
    /// Load：开门 + Mapping。调用方：手动页、E87（Host 认定载具号后）。
    /// </summary>
    public HandleResult<ModuleOperation> Load()
    {
        lock (OperationGate)
        {
            var rejected = CheckLoad();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Load, CreateLoadOperation());
        }
    }

    /// <summary>
    /// Load 能不能做（只查不动）：状态 → 资源 → 互锁。
    /// </summary>
    private HandleResult? CheckLoad()
    {
        return CheckState(LoadPortAction.Load) ?? CheckLoadResource() ?? LoadInterlock();
    }

    /// <summary>
    /// Load 的资源检查：Load 会把这个口的旧账清掉、按扫图重建（片的内部标识全换新），所以这个口账上的片得没被别人占着——
    /// 归还没结束的 Job 的不行（Job 按内部标识认片，重建后就认不出了；要 Load 先中止这个 Job），被搬运占着的槽也不行。
    /// </summary>
    private HandleResult? CheckLoadResource()
    {
        return CheckWafersInJob() ?? CheckSlotsInTransfer();
    }

    /// <summary>
    /// Load 的设备动作：平台默认发驱动的 Load、等完结、把回来的 Mapping 落账（<see cref="TakeSlotMap"/>）。机型动作不一样才重写。
    /// </summary>
    protected virtual ModuleOperation CreateLoadOperation()
    {
        return new LoadPortCommandOperation("Load", () => _driver?.Load(), () => LoadTimeout, TakeSlotMap);
    }

    #endregion

    #region Unload

    public HandleResult<ModuleOperation> Unload()
    {
        return StartUnload(AutoRunMapOnUnload);
    }

    public HandleResult<ModuleOperation> UnloadManually()
    {
        return StartUnload(false);
    }

    public HandleResult? CheckUnload()
    {
        lock (OperationGate)
        {
            return CheckState(LoadPortAction.Unload) ?? CheckCloseDoorResource() ?? UnloadInterlock();
        }
    }

    private HandleResult<ModuleOperation> StartUnload(bool mapAndVerify)
    {
        lock (OperationGate)
        {
            var rejected = CheckUnload();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Unload, CreateUnloadOperation(mapAndVerify));
        }
    }

    protected virtual ModuleOperation CreateUnloadOperation(bool mapAndVerify)
    {
        var operation = new LoadPortCommandOperation("Unload", () => _driver?.Unload(), () => UnloadTimeout);
        if (mapAndVerify)
        {
            operation.Then("Unload 后扫图", () => _driver?.Map(), CheckUnloadSlotMap);
        }

        return operation;
    }

    #endregion

    #region Home

    public HandleResult<ModuleOperation> Home()
    {
        lock (OperationGate)
        {
            var rejected = CheckHome();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Home, CreateHomeOperation());
        }
    }

    private HandleResult? CheckHome()
    {
        return CheckState(LoadPortAction.Home) ?? CheckHomeResource() ?? HomeInterlock();
    }

    private HandleResult? CheckHomeResource()
    {
        if (State != LoadPortState.Loaded)
        {
            return null;
        }

        return CheckCloseDoorResource();
    }

    protected virtual ModuleOperation CreateHomeOperation()
    {
        return new LoadPortCommandOperation("Home", () => _driver?.Home(), () => HomeTimeout);
    }

    #endregion

    #region Clamp / Unclamp

    public HandleResult<ModuleOperation> Clamp()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(LoadPortAction.Clamp) ?? ClampInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Clamp, CreateClampOperation());
        }
    }

    public HandleResult<ModuleOperation> Unclamp()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(LoadPortAction.Unclamp) ?? UnclampInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Unclamp, CreateUnclampOperation());
        }
    }
    protected virtual ModuleOperation CreateClampOperation()
    {
        return new LoadPortCommandOperation("Clamp", () => _driver?.Clamp(), () => ClampTimeout);
    }

    protected virtual ModuleOperation CreateUnclampOperation()
    {
        return new LoadPortCommandOperation("Unclamp", () => _driver?.Unclamp(), () => UnclampTimeout);
    }

    #endregion

    #region Reset / Abort

    public override ModuleOperation? Reset()
    {
        base.Reset();
        lock (OperationGate)
        {
            if (CheckReset() is not null)
            {
                return null;
            }

            return Begin(LoadPortAction.Reset, CreateResetOperation());
        }
    }

    public HandleResult? CheckReset()
    {
        return CheckState(LoadPortAction.Reset);
    }

    public override ModuleOperation? Abort()
    {
        base.Abort();
        lock (OperationGate)
        {
            if (CheckAbort() is not null)
            {
                return null;
            }

            return Begin(LoadPortAction.Abort, CreateAbortOperation());
        }
    }

    public HandleResult? CheckAbort()
    {
        return CheckState(LoadPortAction.Abort);
    }

    protected virtual ModuleOperation CreateResetOperation()
    {
        return new LoadPortCommandOperation("Reset", () => _driver?.ResetDrive(), () => ResetTimeout);
    }

    protected virtual ModuleOperation CreateAbortOperation()
    {
        return new LoadPortCommandOperation("Abort", () => _driver?.Stop(), () => AbortTimeout);
    }

    #endregion

    #region 资源检查（几个动作共用）


    private HandleResult? CheckCloseDoorResource()
    {
        int outside = CountWafersOutside();
        if (outside > 0)
        {
            return HandleResult.Fail(ErrorCodes.PortWafersOutside, Name, outside.ToString(CultureInfo.InvariantCulture));
        }

        return CheckWafersInJob();
    }

    /// <summary>
    /// 这个口账上的片有没有归还没结束的 Job 的（Load 会重建账、Unload / Home 会关门，都会打乱 Job）。没装晶圆账不查。
    /// </summary>
    private HandleResult? CheckWafersInJob()
    {
        var ledger = _waferManager;
        var jobs = _jobs;
        if (ledger is null || !ledger.IsEnable || jobs is null)
        {
            return null;
        }

        foreach (var wafer in ledger.GetSlots(Name))
        {
            if (wafer is null)
            {
                continue;
            }

            string? owner = jobs.OwnerOf(wafer.Id);
            if (owner is not null)
            {
                return HandleResult.Fail(ErrorCodes.PortWafersInJob, Name, owner);
            }
        }

        return null;
    }

    /// <summary>
    /// 这个口有片的槽有没有被搬运占着（Load 会重建账，搬运记着的片就对不上了）。没装晶圆账不查。
    /// </summary>
    private HandleResult? CheckSlotsInTransfer()
    {
        var ledger = _waferManager;
        var transfers = _transfers;
        if (ledger is null || !ledger.IsEnable || transfers is null)
        {
            return null;
        }

        var slots = ledger.GetSlots(Name);
        for (int index = 0; index < slots.Count; index++)
        {
            int slot = index + 1;
            if (slots[index] is not null && transfers.IsSlotLocked(Name, slot))
            {
                return HandleResult.Fail(ErrorCodes.TransferSlotLocked, Name, slot.ToString(CultureInfo.InvariantCulture));
            }
        }

        return null;
    }

    #endregion

    #endregion

    /// <summary>
    /// Load 回来的 Mapping。槽数跟 SC SlotCount 对不上就不落账、Load 判失败：少了的槽会当空槽、往有片的槽上放片，
    /// 多出来的字符在前面整篮错一槽。对得上就交给载具落账；有交叉片、叠片、认不出的槽也判失败（账照落，界面看得到是哪几槽），
    /// 端口停在 Error、机械手不会来——交叉片占着两槽，取放相邻槽、往账上空着的槽回片都可能碰片。两种都报"Mapping 异常"。
    /// </summary>
    private HandleResult TakeSlotMap(LoadPortResponse response)
    {
        var slotMap = response.SlotMap;
        if (slotMap.Count != SlotCount)
        {
            LogHelper.Error(Name, $"Mapping 槽数不对：设备回了 {slotMap.Count} 槽，sc 配的是 {SlotCount} 槽（原文 {response.Content}），没落账");
            return HandleResult.Fail(ErrorCodes.SlotMapLengthMismatch, Name, slotMap.Count.ToString(), SlotCount.ToString());
        }

        UpdateSlotMap(slotMap);

        var abnormalSlots = new List<string>();
        for (int index = 0; index < slotMap.Count; index++)
        {
            if (slotMap[index] is SlotState.CrossSlotted or SlotState.DoubleSlotted or SlotState.Undefined)
            {
                abnormalSlots.Add((index + 1).ToString());
            }
        }

        if (abnormalSlots.Count > 0)
        {
            string slots = string.Join(",", abnormalSlots);
            LogHelper.Error(Name, $"Mapping 有交叉片、叠片或认不出的槽：第 {slots} 槽（原文 {response.Content}）");
            return HandleResult.Fail(ErrorCodes.SlotMapAbnormal, Name, slots);
        }

        return HandleResult.Success();
    }


    private HandleResult CheckUnloadSlotMap(LoadPortResponse response)
    {
        var slotMap = response.SlotMap;
        if (slotMap.Count != SlotCount)
        {
            LogHelper.Error(Name, $"Unload 的 Mapping 槽数不对：设备回了 {slotMap.Count} 槽，sc 配的是 {SlotCount} 槽（原文 {response.Content}），没法对账");
            return HandleResult.Fail(ErrorCodes.SlotMapLengthMismatch, Name, slotMap.Count.ToString(), SlotCount.ToString());
        }

        var mismatchedSlots = new List<string>();
        for (int index = 0; index < slotMap.Count; index++)
        {
            int slot = index + 1;
            var state = slotMap[index];
            bool abnormal = state is SlotState.CrossSlotted or SlotState.DoubleSlotted or SlotState.Undefined;
            bool matched = true;
            if (_waferManager is not null && _waferManager.IsEnable)
            {
                matched = _waferManager.Verify(Name, slot, state != SlotState.Empty);
            }

            if (abnormal || !matched)
            {
                mismatchedSlots.Add(slot.ToString());
            }
        }

        if (mismatchedSlots.Count > 0)
        {
            string slots = string.Join(",", mismatchedSlots);
            LogHelper.Error(Name, $"Unload 时扫到的片跟晶圆账对不上：第 {slots} 槽（原文 {response.Content}）");
            return HandleResult.Fail(ErrorCodes.UnloadSlotMapMismatch, Name, slots);
        }

        LogHelper.Info(Name, "Unload 对账：盒子里的片跟晶圆账一致");
        return HandleResult.Success();
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

    /// <summary>
    /// 能不能动作：停用、驱动没装好之外，驱动没连上也不发——没连上照样发，指令发不出去会落 Error、报"动作失败"，要复位再 Home。
    /// </summary>
    protected override HandleResult? CheckAvailable()
    {
        var unavailable = base.CheckAvailable();
        if (unavailable is not null)
        {
            return unavailable;
        }

        var driver = _driver;
        if (driver is null || !driver.IsConnected)
        {
            return HandleResult.Fail(ErrorCodes.ModuleNotConnected, Name);
        }

        return null;
    }

    /// <summary>
    /// 发起动作（查状态表、挂操作、落执行态），记下是哪个动作、从哪个状态出发（做完按它收尾、复位 / 中止按它判门位）。
    /// 检查都在各动作的入口里（见"动作入口"区），这里不再查联锁。
    /// </summary>
    protected ModuleOperation? Begin(LoadPortAction action, ModuleOperation operation)
    {
        lock (OperationGate)
        {
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

    protected virtual HandleResult? LoadInterlock()
    {
        if (!_carrier.IsArrived)
        {
            return HandleResult.Fail(ErrorCodes.CarrierNotArrived, Name);
        }

        var e84 = E84;
        if (e84 is not null && e84.IsHandoffRunning)
        {
            return HandleResult.Fail(ErrorCodes.E84HandoffRunning, Name);
        }

        return null;
    }

    protected virtual HandleResult? UnloadInterlock()
    {
        return null;
    }

    protected virtual HandleResult? HomeInterlock()
    {
        return null;
    }

    protected virtual HandleResult? ClampInterlock()
    {
        return null;
    }

    protected virtual HandleResult? UnclampInterlock()
    {
        return null;
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
    /// 载具可以供 Job 使用（定片、选回片口、开始 CJ）；正被机械手取放也算已装载。
    /// </summary>
    public bool CanUseCarrierForJob => CheckCarrierForJob() is null;

    /// <summary>
    /// 保持现有载具准入条件：启用、到位、已装载、被认可；不检查机械手是否正占用传片环。
    /// 只查询，不给 Job 分配或占用资源。
    /// </summary>
    public virtual HandleResult? CheckCarrierForJob()
    {
        if (!IsEnabled)
        {
            return HandleResult.Fail(ErrorCodes.ModuleDisabled, Name);
        }

        if (!_carrier.IsArrived)
        {
            return HandleResult.Fail(ErrorCodes.CarrierNotArrived, Name);
        }

        if (!IsLoaded)
        {
            return HandleResult.Fail(ErrorCodes.CarrierNotLoaded, Name);
        }

        return CheckCarrierAcceptance();
    }

    private HandleResult? CheckCarrierAcceptance()
    {
        return _carrier.IsAccepted ? null : HandleResult.Fail(ErrorCodes.CarrierNotAccepted, Name);
    }

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
        CheckCarrierComplete();  //干完了：自动 Unload，或者只对账
        PublishState();  //推送状态
    }

    #region 干完收尾（自动 Unload / 对账）

    /// <summary>上一拍载具的取放状态（没载具为 null），看它从别的变成 Complete 才算"刚干完"。只在扫描线程上读写。</summary>
    private CarrierAccessStatus? _lastAccessStatus;

    /// <summary>载具刚干完、收尾还没做（机械手在收尾、片没回齐）。只在扫描线程上读写。</summary>
    private bool _completePending;

    /// <summary>片没回齐、等着收尾这件事记过日志了，免得每拍记一条。只在扫描线程上读写。</summary>
    private bool _completeWaitLogged;

    /// <summary>
    /// 载具刚干完（Job 做完或中止，Job 管理调载具的 NoteComplete）的收尾，接不接 EAP 都一样（照 CTC）：
    /// EC AutoUnload 开着就 Unload；关着不卸，拿 Load 时的槽图跟晶圆账对一遍（<see cref="CheckCompletedSlotMap"/>），盒子等操作员或 Host CarrierRelease。
    /// 只认"刚变成干完"这一下：操作员把干完的载具又手动 Load 起来，不会马上又被卸掉、又对一遍账。
    /// 两样都先等：机械手还在取放就等它回到 Loaded；从这个口取出去的片还有在腔体、机械手上的（Job 中止时会有），
    /// 门一关片就回不来了、账也必然对不上，等回齐。等完了按那时的 EC 定卸还是对账；干完以后才打开 EC 的不补卸。
    /// 等的时候端口不再是 Load 着的（出错、被人卸了、Home 了、载具拿走了）就不管了，交给人处理。
    /// </summary>
    private void CheckCarrierComplete()
    {
        var accessStatus = _carrier.Info?.AccessStatus;
        if (accessStatus == CarrierAccessStatus.Complete && _lastAccessStatus != CarrierAccessStatus.Complete)
        {
            _completePending = true;
            _completeWaitLogged = false;
        }

        _lastAccessStatus = accessStatus;
        if (!_completePending)
        {
            return;
        }

        if (!IsLoaded || accessStatus != CarrierAccessStatus.Complete)
        {
            _completePending = false;
            return;
        }

        if (State != LoadPortState.Loaded)
        {
            return;
        }

        int outside = CountWafersOutside();
        if (outside > 0)
        {
            if (!_completeWaitLogged)
            {
                _completeWaitLogged = true;
                LogHelper.Warn(Name, $"载具干完了，但从这个口取出去的片还有 {outside} 片不在 LoadPort 上，片回齐了再收尾（自动 Unload 或对账）");
            }

            return;
        }

        _completePending = false;
        if (!AutoUnload)
        {
            CheckCompletedSlotMap();
            return;
        }

        var unload = Unload();
        if (!unload.IsSuccess)
        {
            LogHelper.Warn(Name, $"载具干完了，但 Unload 被拒（{unload.ErrorMessage}：{string.Join(", ", unload.Args)}），等操作员处理");
            return;
        }

        LogHelper.Info(Name, "载具干完了，自动 Unload");
    }

    /// <summary>
    /// 从这个口取出去、现在在腔体或机械手上的片数（晶圆账里来源是本口、位置不在任何 LoadPort 上）；回到别的 LoadPort 的算回来了。没装晶圆账为 0。
    /// </summary>
    private int CountWafersOutside()
    {
        if (_waferManager is null)
        {
            return 0;
        }

        int count = 0;
        foreach (var location in _waferManager.Locations)
        {
            if (_waferManager.IsLoadPort(location.Module))
            {
                continue;
            }

            foreach (var wafer in _waferManager.GetSlots(location.Module))
            {
                if (wafer is not null && string.Equals(wafer.SourceLoadPort, Name, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
        }

        return count;
    }
    private void CheckCompletedSlotMap()
    {
        var waferManager = _waferManager;
        if (waferManager is null || !waferManager.IsEnable)
        {
            return;
        }

        var loadedMap = _carrier.SlotMap;
        if (loadedMap.Count != SlotCount)
        {
            LogHelper.Warn(Name, $"载具干完了，但 Load 时的槽图是 {loadedMap.Count} 槽、sc 配的是 {SlotCount} 槽，没法对账");
            return;
        }

        // 从这个口出去、回到别的 LoadPort 上的片，按来处的槽号记下
        var returnedElsewhere = new HashSet<int>();
        foreach (var location in waferManager.Locations)
        {
            if (!waferManager.IsLoadPort(location.Module) || string.Equals(location.Module, Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var wafer in waferManager.GetSlots(location.Module))
            {
                if (wafer is not null && string.Equals(wafer.SourceLoadPort, Name, StringComparison.OrdinalIgnoreCase))
                {
                    returnedElsewhere.Add(wafer.SourceSlot);
                }
            }
        }

        var slots = waferManager.GetSlots(Name);
        var mismatched = new List<string>();
        for (int index = 0; index < loadedMap.Count; index++)
        {
            int slot = index + 1;
            var wafer = index < slots.Count ? slots[index] : null;
            bool hadWafer = loadedMap[index] != SlotState.Empty;
            bool fromHere = wafer is not null && string.Equals(wafer.SourceLoadPort, Name, StringComparison.OrdinalIgnoreCase);
            bool isOriginal = fromHere && wafer is not null && wafer.SourceSlot == slot;
            bool fromOtherPort = wafer is not null && wafer.SourceLoadPort is not null && !fromHere;

            // ① Load 时这一槽的片：还在这一槽，或者回到了别的 LoadPort
            if (hadWafer && !isOriginal && !returnedElsewhere.Contains(slot))
            {
                mismatched.Add($"第 {slot} 槽 Load 时有片、账上找不到这一片");
                continue;
            }

            // ② 账上这一槽的片说得清来路：Load 时就在这一槽的那一片，或者别的 LoadPort 来的片
            if (wafer is not null && !(hadWafer && isOriginal) && !fromOtherPort)
            {
                mismatched.Add(fromHere
                    ? $"第 {slot} 槽账上多了片（是这盒第 {wafer.SourceSlot} 槽的 {wafer.WaferId}）"
                    : $"第 {slot} 槽账上多了片（{wafer.WaferId}，不是从 LoadPort 来的）");
            }
        }

        if (mismatched.Count == 0)
        {
            LogHelper.Info(Name, "载具干完了（EC AutoUnload 关着，不卸）：Load 时的槽图跟晶圆账一致");
            return;
        }

        LogHelper.Error(Name, $"载具干完了，Load 时的槽图跟晶圆账对不上：{string.Join("；", mismatched)}");
        RaiseAlarm(SlotMapAlarm);
    }

    #endregion

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

        // 设备做完了、Mapping 不能用：报专门的一条，看报警就知道去查槽数、理片
        if (operation.Code == ErrorCodes.SlotMapLengthMismatch || operation.Code == ErrorCodes.SlotMapAbnormal
            || operation.Code == ErrorCodes.UnloadSlotMapMismatch)
        {
            RaiseAlarm(SlotMapAlarm);
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

        _notifier?.Post(() =>
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

        _notifier?.Post(() =>
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
