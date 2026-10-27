using System.Globalization;
using xyz.Common.Log;
using xyz.Drivers.Communication;
using xyz.Drivers.Device;
using xyz.Drivers.Loadport;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

public abstract class BaseLoadPortModule : BaseTransferStationModule, ILoadPort, IWaferMap
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

    #region Machine 

    [Machine("True", "LoadPort", "是否启用本 LoadPort (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    [Machine("25", "LoadPort", "花篮槽数")]
    public override int SlotCount { get; set; } = 25;

    [Machine("False", "LoadPort",
        "自动跑货的 Unload（Job 干完自动卸、Host 放行或取消）要不要再扫一遍图跟晶圆账对：True = 关门前先扫一遍（自己扫，或机械手开了扫片就叫它扫）核对，对不上 Unload 判失败、报警、门不关；False = 直接关门，不扫、不对账。" +
        "这个开关只管自动跑货用不用它；手动卸载不受影响，一律不扫图、不对账")]
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
        @default: "30000", description: "Load 动作超时（开门、自己扫片、取槽图每条各按它算；等机械手扫片不算）")]
    public int LoadTimeout
    {
        get { return GetEcInt(nameof(LoadTimeout)); }
        set { SetEcInt(nameof(LoadTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "30000", description: "Unload 动作超时（关门；自动跑货带图对账时，关门前的扫片、取槽图每条各按它算，等机械手扫片不算）")]
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
    [Alarm("LoadPort Load 超时", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "Load（开门、扫片、取槽图）有一条没在 EC LoadTimeout 内做完", Solution = "检查门、Mapping 传感器和 FOUP 有没有放好，看 LoadPort 有没有报错；处理好后复位，再 Home")]
    public string LoadTimeoutAlarm = nameof(LoadTimeoutAlarm);

    [Alarm("LoadPort Unload 超时", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "Unload（关门，或自动跑货对账时关门前的扫片、取槽图）有一条没在 EC UnloadTimeout 内做完", Solution = "检查门有没有被挡、FOUP 有没有放好；处理好后复位，再 Home")]
    public string UnloadTimeoutAlarm = nameof(UnloadTimeoutAlarm);

    [Alarm("LoadPort 初始化超时", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "Home（初始化回原点）没在 EC HomeTimeout 内做完", Solution = "检查串口连接、LoadPort 硬件状态及供电")]
    public string InitTimeoutAlarm = nameof(InitTimeoutAlarm);

    [Alarm("LoadPort 夹紧超时", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "夹紧 FOUP 没在 EC ClampTimeout 内做完", Solution = "检查 FOUP 有没有放到位、夹爪有没有卡住；处理好后复位，再 Home")]
    public string ClampTimeoutAlarm = nameof(ClampTimeoutAlarm);

    [Alarm("LoadPort 松开超时", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "松开 FOUP 没在 EC UnclampTimeout 内做完", Solution = "检查夹爪有没有卡住；处理好后复位，再 Home")]
    public string UnclampTimeoutAlarm = nameof(UnclampTimeoutAlarm);

    [Alarm("LoadPort 复位超时", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "复位（清设备报错）没在 EC ResetTimeout 内做完", Solution = "检查串口 / 网口连接和 LoadPort 供电，再复位")]
    public string ResetTimeoutAlarm = nameof(ResetTimeoutAlarm);

    [Alarm("LoadPort 中止超时", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "中止（急停）没在 EC AbortTimeout 内做完", Solution = "检查串口 / 网口连接和 LoadPort 状态；处理好后复位，再 Home")]
    public string AbortTimeoutAlarm = nameof(AbortTimeoutAlarm);

    // 不是超时的失败：设备回了错、指令发不出去
    [Alarm("LoadPort 动作失败", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "Load、Unload、Home、夹紧、松开等动作被设备回错或指令发不出去，LoadPort 停在错误状态", Solution = "看报警前后的日志找失败原因，处理好后复位，再 Home")]
    public string ControlledStopAlarm = nameof(ControlledStopAlarm);

    [Alarm("LoadPort 设备报警", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Light, Description = "LoadPort 设备本身报警", Solution = "检查 LoadPort 硬件/通讯状态")]
    public string LoadPortDeviceAlarm = nameof(LoadPortDeviceAlarm);

    // 设备做完了、但 Mapping 结果不能用：槽数对不上，有交叉片、叠片、认不出的槽，或者 Unload 时扫到的跟账对不上，都报这一条；
    // EC AutoUnload 关着时载具干完、Load 时的槽图跟晶圆账对不上也报这一条（只报警）
    [Alarm("LoadPort Mapping 异常", AlarmCategory.DataIntegrity, AlarmLevel = AlarmLevel.Light, Description = "Load 时扫到的、或自动跑货 Unload 关门前扫到的 Mapping 不能用：槽数跟 machine.xml 的 SlotCount 对不上，有交叉片、叠片、认不出的槽，或者 Unload 时扫到的片跟晶圆账对不上；动作判失败，LoadPort 停在错误状态（Unload 对账不符时门没关）。另外 EC AutoUnload 关着时载具干完、Load 时的槽图跟晶圆账对不上也报这一条，这种只报警，端口不停", Solution = "看报警前后的日志：槽数不对就核对 LoadPort 设备（或扫片机械手）的槽数设置和 machine.xml 的 SlotCount；交叉片、叠片就复位、Home 关门，把盒子拿下来理好片再放上 Load；Unload 对账不符就按实物在账单调整页改账，再复位、Home；干完对账不符就按日志里的槽号查实物，在账单调整页改账")]
    public string SlotMapAlarm = nameof(SlotMapAlarm);

    #endregion

    #region Element

    /// <summary>
    /// 设备通道（machine.xml 里本端口下启用的那条：串口或网口）：项目的 LoadPort 类用它发指令
    /// </summary>
    public ICommDriver? _driver { get; private set; }

    /// <summary>
    /// 这台 LoadPort 的解码器：开机时建一次交给通道
    /// </summary>
    private IFrameCodec? _codec;

    public ICarrierIdReader? _rfid { get; private set; }

    public ICarrier _carrier { get; private set; } = null!;

    public ICarrierAccess Carrier => _carrier;

    private ILedgerManager? _ledger;

    /// <summary>
    /// 映射元件：元件初始化时取一次，账上的片转给界面用
    /// </summary>
    private IMapper? _mapper;

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

    protected override int StandbyState => LoadPortState.Loaded;

    /// <summary>
    /// 站点类型：LoadPort
    /// </summary>
    public override StationKind Kind => StationKind.LoadPort;

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

    #region 元件初始化与驱动连接

    /// <summary>
    /// 元件初始化
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public override bool InitElement()
    {
        #region 载具

        // 停用的端口也要挂上载具，所以放在判 IsEnable 前面：停用的口照样扫描、推状态，Job 按载具号找端口也会挨个查它的载具，
        // 没挂就是空引用（扫描每拍抛、状态推不出去，有一个口停用就建不了 Job）。
        var carrier = FindChild<ICarrier>();
        if (carrier is null)
        {
            throw new InvalidOperationException($"LoadPort {Name} 的 machine.xml 节点下没配 _carrier 子元件（实现 ICarrier 的元件，平台默认 Type=xyz.Elements.Elements.CarrierElement）。");
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

        #region 设备通道

        // 通道打开之前把解码器交给它；先摘后挂：初始化可重入，保证只挂一份；先挂再连，连上就可能有放上 / 拿走的主动上报
        var driver = CommDriverElement.FindIn(this);
        if (driver is not null)
        {
            _codec ??= CreateCodec();
            driver.UseCodec(_codec);
            driver.Reported -= OnReport;
            driver.Reported += OnReport;
            _driver = driver;
        }
        else
        {
            LogHelper.Error(this, "machine.xml 没有启用的设备通道：本端口下要挂串口 SerialDriverElement 或网口 TcpDriverElement，并启用一条（IsEnable=True）");
        }

        #endregion

        ///晶圆账注册
        _ledger = IocHelper.GetService<ILedgerManager>();
        _ledger?.RegisterLoadPort(Name, SlotCount);
        _mapper = IocHelper.GetService<IMapper>();

        bool childrenInitialized = base.InitElement();
        return driver is not null && childrenInitialized;
    }

    private IJobManager? _jobs;
    private ITransferManager? _transfers;
    private IEapNotifierElement? _notifier;

    /// <summary>
    /// 替这个口扫片的机械手（machine.xml 开了 MapEnabled、示教点表里有这个口）；没有是 null，自己扫
    /// </summary>
    private ITransferRobot? _mapRobot;

    public override void Bind()
    {
        _jobs = IocHelper.GetService<IJobManager>();
        _transfers = IocHelper.GetService<ITransferManager>();
        _notifier = IocHelper.GetService<IEapNotifierElement>();
        _mapRobot = FindMapRobot();
        if (_mapRobot is not null && _transfers is null)
        {
            throw new InvalidOperationException($"{Name} 要机械手 {_mapRobot.Name} 扫片，要经搬运管理排队，但 machine.xml 没配 Transfer 节点。");
        }
    }

    /// <summary>
    /// 开机认替这个口扫片的机械手：开了 MapEnabled、示教点表里有这个口的；两台都是就是配错了，开机报出来
    /// </summary>
    private ITransferRobot? FindMapRobot()
    {
        ITransferRobot? found = null;
        foreach (var robot in IocHelper.GetServices<ITransferRobot>())
        {
            if (!robot.MapEnabled || !robot.TryGetTeachPoint(Name, out _))
            {
                continue;
            }

            if (found is not null)
            {
                throw new InvalidOperationException(
                    $"machine.xml 里机械手 {found.Name}、{robot.Name} 都开了 MapEnabled、示教点表里都有 {Name}，不知道该谁扫这个口，只能留一台。");
            }

            found = robot;
        }

        if (found is not null)
        {
            LogHelper.Info(this, $"扫片由机械手 {found.Name} 做");
        }

        return found;
    }

    public void Close()
    {
        _rfid?.Close();
        _driver?.Close();
    }

    /// <summary>
    /// 设备主动上报（没有在途指令认的报文，在通道的解析线程上调）：项目的 LoadPort 类认自己品牌的报文，放上 / 拿走交给 SetDeviceReportedPlaced
    /// </summary>
    protected virtual void OnReport(object message)
    {
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

    private StatusQueryLoop? _queries;

    /// <summary>
    /// 设备状态轮询（扫描线程）：节奏是平台的（同一时间一条在途、超时作废、一圈查完满 EC QueryStatusIntervalMs 再开下一圈），一圈查什么由 QueryDataLoop 给。
    /// 查到的值由 QueryDataLoop 自己写进 Status，这里只处理出事的：没连上、查询超时都把状态清空——查到之前在位保持原判断（载具只认查到的）
    /// </summary>
    private void LoopQueryStatus()
    {
        var driver = _driver;
        if (!IsEnable || driver is null)
        {
            return;
        }

        _queries ??= new StatusQueryLoop(this);
        var happened = _queries.Scan(driver, QueryDataLoop, QueryStatusIntervalMs, QueryDataTimeOut);
        if (happened == StatusQueryEvent.TimedOut)
        {
            Status = null;
            return;
        }

        if (happened == StatusQueryEvent.Disconnected)
        {
            Status = null;
            OnDisconnected();
        }
    }

    /// <summary>
    /// 一圈查什么、回来写到哪，从上往下写：yield return 一条 = 平台替它发出去、等它回来（或超时作废）再往下走；
    /// 查到的设备状态写进 Status。默认不查（全靠设备主动推的品牌）
    /// </summary>
    protected virtual IEnumerable<DeviceCommand> QueryDataLoop()
    {
        yield break;
    }

    /// <summary>
    /// 连接刚断（断开那一拍调一次，扫描线程）：跟着连接走的东西在这里清掉，连上以后重来
    /// </summary>
    protected virtual void OnDisconnected()
    {
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
            LedgerSlots = WaferLedgerSnapshot.SlotsOf(_ledger, _mapper, Name),
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

            case SlotState.Normal:
                return LoadPortSlotState.CorrectlyOccupied;

            case SlotState.Double:
                return LoadPortSlotState.DoubleSlotted;

            case SlotState.Crossed:
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
        RemoteEventBus.Send(dto, Name);
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
    /// 模块初始化 = Home；门开着、从这个口取出去的片还在机内时不 Home（回片要用这个口），算初始化好了
    /// </summary>
    public override HandleResult<ModuleRoutine> InitModule()
    {
        if (State == LoadPortState.Loaded)
        {
            int outside = CountWafersOutside();
            if (outside > 0)
            {
                LogHelper.Info(this, $"初始化：门开着，从这个口取出去的 {outside} 片还在机内，不 Home（回片要用这个口）");
                return HandleResult<ModuleRoutine>.Success(ModuleRoutine.Done(this, "Init"));
            }
        }

        return Home();
    }

    #region 动作入口：状态 → 资源 → 互锁

    /// <summary>
    /// 状态 → 资源 → 互锁 +原因
    /// </summary>
    /// <param name="action"></param>
    /// <param name="routine"></param>
    /// <returns></returns>
    private HandleResult<ModuleRoutine> Started(LoadPortAction action, ModuleRoutine routine)
    {
        var started = Begin(action, routine);
        if (started is null)
        {
            return HandleResult<ModuleRoutine>.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
        }

        return HandleResult<ModuleRoutine>.Success(started);
    }

    #region Load

    public HandleResult<ModuleRoutine> Load()
    {
        lock (RoutineGate)
        {
            var rejected = CheckLoad();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Load, CreateLoadRoutine());
        }
    }

    private HandleResult? CheckLoad()
    {
        return CheckState(LoadPortAction.Load) ?? CheckLoadResource() ?? LoadInterlock();
    }

    private HandleResult? CheckLoadResource()
    {
        return CheckWafersInJob() ?? CheckSlotsInTransfer();
    }

    /// <summary>
    /// Load 的步骤：先只装载开门，再扫片（自己扫或叫机械手扫），扫到的槽图落账；整个做法不一样才重写
    /// </summary>
    protected virtual ModuleRoutine CreateLoadRoutine()
    {
        var load = new ModuleRoutine(this, "Load", _driver, () => LoadTimeout);
        AddLoadSteps(load);
        AddMapStages(load, TakeSlotMap);
        return load;
    }

    /// <summary>
    /// 装载开门那几条（不扫片）：项目的 LoadPort 类用自己品牌的指令加 Command 步骤（超时用 EC LoadTimeout）
    /// </summary>
    protected abstract void AddLoadSteps(ModuleRoutine load);

    #endregion

    #region Unload

    public HandleResult<ModuleRoutine> Unload()
    {
        return StartUnload(AutoRunMapOnUnload);
    }

    public HandleResult<ModuleRoutine> UnloadManually()
    {
        return StartUnload(false);
    }

    public HandleResult? CheckUnload()
    {
        lock (RoutineGate)
        {
            return CheckState(LoadPortAction.Unload) ?? CheckCloseDoorResource() ?? UnloadInterlock();
        }
    }

    private HandleResult<ModuleRoutine> StartUnload(bool mapAndVerify)
    {
        lock (RoutineGate)
        {
            var rejected = CheckUnload();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Unload, CreateUnloadRoutine(mapAndVerify));
        }
    }

    /// <summary>
    /// Unload 的步骤：要对账就先扫片（自己扫或叫机械手扫）跟晶圆账对，对得上再关门——门关了就扫不了；整个做法不一样才重写
    /// </summary>
    protected virtual ModuleRoutine CreateUnloadRoutine(bool mapAndVerify)
    {
        var unload = new ModuleRoutine(this, "Unload", _driver, () => UnloadTimeout);
        if (mapAndVerify)
        {
            AddMapStages(unload, CheckUnloadSlotMap);
        }

        AddUnloadSteps(unload);
        return unload;
    }

    /// <summary>
    /// 关门那几条：项目的 LoadPort 类用自己品牌的指令加 Command 步骤（超时用 EC UnloadTimeout）
    /// </summary>
    protected abstract void AddUnloadSteps(ModuleRoutine unload);

    #endregion

    #region Home

    public HandleResult<ModuleRoutine> Home()
    {
        lock (RoutineGate)
        {
            var rejected = CheckHome();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Home, CreateHomeRoutine());
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

    /// <summary>
    /// Home 的步骤：项目的 AddHomeSteps 加的那几条；整个做法不一样才重写
    /// </summary>
    protected virtual ModuleRoutine CreateHomeRoutine()
    {
        var home = new ModuleRoutine(this, "Home", _driver, () => HomeTimeout);
        AddHomeSteps(home);
        return home;
    }

    /// <summary>
    /// 回零那几条（整机回零）：项目的 LoadPort 类用自己品牌的指令加 Command 步骤（超时用 EC HomeTimeout）
    /// </summary>
    protected abstract void AddHomeSteps(ModuleRoutine home);

    #endregion

    #region Clamp / Unclamp

    public HandleResult<ModuleRoutine> Clamp()
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(LoadPortAction.Clamp) ?? ClampInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Clamp, CreateClampRoutine());
        }
    }

    public HandleResult<ModuleRoutine> Unclamp()
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(LoadPortAction.Unclamp) ?? UnclampInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Unclamp, CreateUnclampRoutine());
        }
    }

    /// <summary>
    /// 夹紧的步骤：项目的 AddClampSteps 加的那几条；整个做法不一样才重写
    /// </summary>
    protected virtual ModuleRoutine CreateClampRoutine()
    {
        var clamp = new ModuleRoutine(this, "Clamp", _driver, () => ClampTimeout);
        AddClampSteps(clamp);
        return clamp;
    }

    /// <summary>
    /// 松开的步骤：项目的 AddUnclampSteps 加的那几条；整个做法不一样才重写
    /// </summary>
    protected virtual ModuleRoutine CreateUnclampRoutine()
    {
        var unclamp = new ModuleRoutine(this, "Unclamp", _driver, () => UnclampTimeout);
        AddUnclampSteps(unclamp);
        return unclamp;
    }

    /// <summary>
    /// 夹紧那几条：项目的 LoadPort 类用自己品牌的指令加 Command 步骤（超时用 EC ClampTimeout）
    /// </summary>
    protected abstract void AddClampSteps(ModuleRoutine clamp);

    /// <summary>
    /// 松开那几条：项目的 LoadPort 类用自己品牌的指令加 Command 步骤（超时用 EC UnclampTimeout）
    /// </summary>
    protected abstract void AddUnclampSteps(ModuleRoutine unclamp);

    #endregion

    #region 扫片（IWaferMap）

    // LoadPort 自带扫片传感器，所以也实现 IWaferMap：Map 用自己的传感器单独扫一遍（Loaded 时），扫到的存进 MapResults、不落账。
    // Load、自动跑货 Unload 对账里的扫片走 AddMapStages：有机械手开了扫片、示教点表里有这个口，就交搬运管理排它去扫，不然自己扫。

    private readonly object _mapGate = new();

    /// <summary>
    /// 自己最近一次扫到的结果；开扫时先清掉
    /// </summary>
    private WaferMapResult? _mapResult;

    /// <summary>
    /// 自己最近一次扫到的结果（槽图 + 设备回的原文），键是这个口的名字；开扫时先删掉
    /// </summary>
    public IReadOnlyDictionary<string, WaferMapResult> MapResults
    {
        get
        {
            var results = new Dictionary<string, WaferMapResult>(StringComparer.OrdinalIgnoreCase);
            lock (_mapGate)
            {
                if (_mapResult is not null)
                {
                    results[Name] = _mapResult;
                }
            }

            return results;
        }
    }

    /// <summary>
    /// 用自己的扫片传感器单独扫一遍：只扫自己、门开着（Loaded）才收；扫到的存进 MapResults，不落账
    /// </summary>
    public HandleResult<ModuleRoutine> Map(string station)
    {
        lock (RoutineGate)
        {
            var rejected = CheckMapStation(station) ?? CheckState(LoadPortAction.Map) ?? MapInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Map, CreateMapRoutine());
        }
    }

    /// <summary>
    /// 只扫自己：站点名要是这个口
    /// </summary>
    private HandleResult? CheckMapStation(string station)
    {
        if (string.Equals(station, Name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return HandleResult.Fail(ErrorCodes.StationNotFound, Name, station);
    }

    /// <summary>
    /// 单独扫一遍的步骤：扫片、取槽图；整个做法不一样才重写
    /// </summary>
    protected virtual ModuleRoutine CreateMapRoutine()
    {
        var map = new ModuleRoutine(this, "Map", _driver, () => LoadTimeout);
        AddOwnMapStages(map, null);
        return map;
    }

    /// <summary>
    /// 自己扫片那几条（扫、取槽图）：项目的 LoadPort 类用自己品牌的指令加 Command 步骤，拿到槽图连同设备回的原文调 SaveMapResult 存起来（超时用 EC LoadTimeout / UnloadTimeout）
    /// </summary>
    protected abstract void AddMapSteps(ModuleRoutine map);

    /// <summary>
    /// 加上扫片：有机械手替这个口扫，就交搬运管理排它去扫，不然自己扫；扫到的结果交给 check（Load 落账、Unload 对账）
    /// </summary>
    private void AddMapStages(ModuleRoutine routine, Func<WaferMapResult, HandleResult> check)
    {
        var robot = _mapRobot;
        if (robot is null)
        {
            AddOwnMapStages(routine, check);
            return;
        }

        routine.Start($"{robot.Name} 扫片", () => StartRobotMap(robot), () => check(RobotMapResult(robot)));
    }

    /// <summary>
    /// 自己扫：先清掉上一次的结果，加上项目给的扫片那几条，扫完把存下的结果交给 check（check 为 null 只存不用）
    /// </summary>
    private void AddOwnMapStages(ModuleRoutine routine, Func<WaferMapResult, HandleResult>? check)
    {
        routine.Do("清掉旧槽图", ClearMapResult);
        AddMapSteps(routine);
        routine.Do("用槽图", () =>
        {
            if (check is null)
            {
                return HandleResult.Success();
            }

            return check(OwnMapResult());
        });
    }

    private HandleResult ClearMapResult()
    {
        lock (_mapGate)
        {
            _mapResult = null;
        }

        return HandleResult.Success();
    }

    /// <summary>
    /// 存自己扫到的槽图（下标 0 是第 1 槽）和设备回的原文（各家自己的写法，平台只拿来写日志），给 MapResults 读；项目的扫片那几条拿到图时调
    /// </summary>
    protected void SaveMapResult(IReadOnlyList<SlotState> slotMap, string raw)
    {
        lock (_mapGate)
        {
            _mapResult = new WaferMapResult { Slots = slotMap.ToArray(), Raw = raw };
        }
    }

    /// <summary>
    /// 自己这次扫到的结果；扫片那几条没存上给空的，落账、对账按槽数对不上处理
    /// </summary>
    private WaferMapResult OwnMapResult()
    {
        lock (_mapGate)
        {
            if (_mapResult is null)
            {
                return new WaferMapResult();
            }

            return _mapResult;
        }
    }

    /// <summary>
    /// 叫搬运管理排机械手来扫这个口（开机已查过搬运管理装了）
    /// </summary>
    private HandleResult<ModuleRoutine> StartRobotMap(ITransferRobot robot)
    {
        if (_transfers is null)
        {
            return HandleResult<ModuleRoutine>.Fail(ErrorCodes.TransferNotInstalled);
        }

        return _transfers.StartMap(robot.Name, Name);
    }

    /// <summary>
    /// 机械手扫到的这个口的结果；没存上给空的，落账、对账按槽数对不上处理
    /// </summary>
    private WaferMapResult RobotMapResult(ITransferRobot robot)
    {
        if (robot is IWaferMap mapper && mapper.MapResults.TryGetValue(Name, out var result))
        {
            return result;
        }

        return new WaferMapResult();
    }

    #endregion

    #region Reset / Abort

    public override HandleResult<ModuleRoutine> Reset()
    {
        base.Reset();
        lock (RoutineGate)
        {
            var rejected = CheckState(LoadPortAction.Reset);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Reset, CreateResetRoutine());
        }
    }

    public override HandleResult<ModuleRoutine> Abort()
    {
        base.Abort();
        lock (RoutineGate)
        {
            var rejected = CheckState(LoadPortAction.Abort);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(LoadPortAction.Abort, CreateAbortRoutine());
        }
    }

    /// <summary>
    /// 复位的步骤：项目的 AddResetSteps 加的那几条；整个做法不一样才重写
    /// </summary>
    protected virtual ModuleRoutine CreateResetRoutine()
    {
        var reset = new ModuleRoutine(this, "Reset", _driver, () => ResetTimeout);
        AddResetSteps(reset);
        return reset;
    }

    /// <summary>
    /// 中止的步骤：项目的 AddAbortSteps 加的那几条；整个做法不一样才重写
    /// </summary>
    protected virtual ModuleRoutine CreateAbortRoutine()
    {
        var abort = new ModuleRoutine(this, "Abort", _driver, () => AbortTimeout);
        AddAbortSteps(abort);
        return abort;
    }

    /// <summary>
    /// 复位那几条（清设备报错）：项目的 LoadPort 类用自己品牌的指令加 Command 步骤（超时用 EC ResetTimeout）
    /// </summary>
    protected abstract void AddResetSteps(ModuleRoutine reset);

    /// <summary>
    /// 中止那几条（急停）：项目的 LoadPort 类用自己品牌的指令加 Command 步骤（超时用 EC AbortTimeout）；被顶掉的在途指令平台已经作废
    /// </summary>
    protected abstract void AddAbortSteps(ModuleRoutine abort);

    /// <summary>
    /// 这台 LoadPort 的解码器（拆帧、解报文），开机时交给通道
    /// </summary>
    protected abstract IFrameCodec CreateCodec();

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
    /// 有没有wafer正在job
    /// </summary>
    /// <returns></returns>
    private HandleResult? CheckWafersInJob()
    {
        if (_ledger is null || !_ledger.IsEnable || _jobs is null)
        {
            return null;
        }

        foreach (var wafer in _ledger.GetSlots(Name))
        {
            if (wafer is null)
            {
                continue;
            }

            string? owner = _jobs.OwnerOf(wafer.Id);
            if (owner is not null)
            {
                return HandleResult.Fail(ErrorCodes.PortWafersInJob, Name, owner);
            }
        }

        return null;
    }

    /// <summary>
    /// 有没有wafer正在搬运
    /// </summary>
    private HandleResult? CheckSlotsInTransfer()
    {
        if (_ledger is null || !_ledger.IsEnable || _transfers is null)
        {
            return null;
        }

        var slots = _ledger.GetSlots(Name);
        for (int index = 0; index < slots.Count; index++)
        {
            int slot = index + 1;
            if (slots[index] is not null && _transfers.IsSlotLocked(Name, slot))
            {
                return HandleResult.Fail(ErrorCodes.TransferSlotLocked, Name, slot.ToString(CultureInfo.InvariantCulture));
            }
        }

        return null;
    }

    #endregion

    #endregion

    /// <summary>
    /// Load 时扫到的槽图落账：槽数对不上不落账，有交叉片、叠片、认不出的槽落了账也判失败
    /// </summary>
    private HandleResult TakeSlotMap(WaferMapResult map)
    {
        var slotMap = map.Slots;
        if (slotMap.Count != SlotCount)
        {
            LogHelper.Error(this, $"Mapping 槽数不对：扫到 {slotMap.Count} 槽，machine.xml 配的是 {SlotCount} 槽{RawText(map)}，没落账");
            return HandleResult.Fail(ErrorCodes.SlotMapLengthMismatch, Name, slotMap.Count.ToString(), SlotCount.ToString());
        }

        UpdateSlotMap(slotMap);

        var abnormalSlots = new List<string>();
        for (int index = 0; index < slotMap.Count; index++)
        {
            if (slotMap[index] is SlotState.Crossed or SlotState.Double or SlotState.Undefined)
            {
                abnormalSlots.Add((index + 1).ToString());
            }
        }

        if (abnormalSlots.Count > 0)
        {
            string slots = string.Join(",", abnormalSlots);
            LogHelper.Error(this, $"Mapping 有交叉片、叠片或认不出的槽：第 {slots} 槽{RawText(map)}");
            return HandleResult.Fail(ErrorCodes.SlotMapAbnormal, Name, slots);
        }

        return HandleResult.Success();
    }

    /// <summary>
    /// 自动跑货 Unload 关门前扫到的槽图跟晶圆账对：对不上判失败，账不改、载具槽图不动
    /// </summary>
    private HandleResult CheckUnloadSlotMap(WaferMapResult map)
    {
        var slotMap = map.Slots;
        if (slotMap.Count != SlotCount)
        {
            LogHelper.Error(this, $"Unload 的 Mapping 槽数不对：扫到 {slotMap.Count} 槽，machine.xml 配的是 {SlotCount} 槽{RawText(map)}，没法对账");
            return HandleResult.Fail(ErrorCodes.SlotMapLengthMismatch, Name, slotMap.Count.ToString(), SlotCount.ToString());
        }

        var mismatchedSlots = new List<string>();
        for (int index = 0; index < slotMap.Count; index++)
        {
            int slot = index + 1;
            var state = slotMap[index];
            bool abnormal = state is SlotState.Crossed or SlotState.Double or SlotState.Undefined;
            bool matched = true;
            if (_ledger is not null && _ledger.IsEnable)
            {
                matched = _ledger.Verify(Name, slot, state != SlotState.Empty);
            }

            if (abnormal || !matched)
            {
                mismatchedSlots.Add(slot.ToString());
            }
        }

        if (mismatchedSlots.Count > 0)
        {
            string slots = string.Join(",", mismatchedSlots);
            LogHelper.Error(this, $"Unload 时扫到的片跟晶圆账对不上：第 {slots} 槽{RawText(map)}，门不关");
            return HandleResult.Fail(ErrorCodes.UnloadSlotMapMismatch, Name, slots);
        }

        LogHelper.Info(this, "Unload 对账：盒子里的片跟晶圆账一致");
        return HandleResult.Success();
    }

    /// <summary>
    /// 日志里带上设备回的原文（各家自己的写法，平台不认、原样写）；没有原文（扫片那几条没存上）就不带
    /// </summary>
    private static string RawText(WaferMapResult map)
    {
        if (map.Raw.Length == 0)
        {
            return string.Empty;
        }

        return $"（设备回的：{map.Raw}）";
    }

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
    /// 发起动作（查状态表、挂动作、落执行态），记下是哪个动作、从哪个状态出发（做完按它收尾、复位 / 中止按它判门位）。
    /// 检查都在各动作的入口里（见"动作入口"区），这里不再查联锁。
    /// </summary>
    protected ModuleRoutine? Begin(LoadPortAction action, ModuleRoutine routine)
    {
        lock (RoutineGate)
        {
            int from = State;
            var started = base.Begin(action, routine);
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

    protected virtual HandleResult? MapInterlock()
    {
        return null;
    }

    #endregion

    /// <summary>
    /// 动作完成之后的钩子
    /// </summary>
    /// <param name="routine"></param>
    protected override void OnRoutineCompleted(ModuleRoutine routine)
    {
        RaiseActionFailedAlarm(_action, routine);

        //动作失败
        if (!routine.IsSuccess)
        {
            _driver?.AbandonAll("Abandoned");  //作废指令
            string reason = routine.Reason;

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
            if (!IsEnable || Mode != ModuleMode.Production || State == ModuleState.NotInit || State == ModuleState.Error)
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
    /// 载具准入条件：启用、到位、已装载、已有有效载具号、被认可；不检查机械手是否正占用传片环。
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

        if (string.IsNullOrWhiteSpace(_carrier.CarrierId))
        {
            return HandleResult.Fail(ErrorCodes.CarrierIdRequired, Name);
        }

        return CheckCarrierAcceptance();
    }

    /// <summary>
    /// 接收 CJ 结束通知，由载具按 Job 保存的载具号核对并完成；自动卸载仍由端口扫描等待回片后处理。
    /// 机型可重写补充收尾条件，标记载具完成时继续调用带载具号的入口。
    /// </summary>
    public virtual HandleResult NotifyJobFinished(string carrierId)
    {
        return _carrier.NoteComplete(carrierId);
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

    /// <summary>
    /// 载具的取放状态 最近一次的状态
    /// </summary>
    private CarrierAccessStatus? _lastAccessStatus;

    /// <summary>
    /// 载具刚干完、收尾还没做
    /// </summary>
    private bool _completePending;

    private bool _completeWaitLogged;

    /// <summary>
    /// 载具完成
    /// </summary>
    private void CheckCarrierComplete()
    {
        //载具状态
        var accessStatus = _carrier.Info?.AccessStatus;
        //刚完成
        if (accessStatus == CarrierAccessStatus.Complete && _lastAccessStatus != CarrierAccessStatus.Complete)
        {
            _completePending = true;
            _completeWaitLogged = false;
        }
        //临时记录状态
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
                LogHelper.Warn(this, $"载具干完了，但从这个口取出去的片还有 {outside} 片不在 LoadPort 上，片回齐了再收尾（自动 Unload 或对账）");
            }

            return;
        }

        _completePending = false;
        //检查是都要unload map
        if (!AutoUnload)
        {
            CheckCompletedSlotMap();
            return;
        }

        var unload = Unload();
        if (!unload.IsSuccess)
        {
            LogHelper.Warn(this, $"载具干完了，但 Unload 被拒（{unload.ErrorMessage}：{string.Join(", ", unload.Args)}），等操作员处理");
            return;
        }

        LogHelper.Info(this, "载具干完了，自动 Unload");
    }

   /// <summary>
   /// c从这个口出去多少片
   /// </summary>
   /// <returns></returns>
    private int CountWafersOutside()
    {
        if (_ledger is null)
        {
            return 0;
        }

        int count = 0;
        foreach (var location in _ledger.Locations)
        {
            if (_ledger.IsLoadPort(location.Module))
            {
                continue;
            }

            foreach (var wafer in _ledger.GetSlots(location.Module))
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
        if (_ledger is null || !_ledger.IsEnable)
        {
            return;
        }

        var loadedMap = _carrier.SlotMap;
        if (loadedMap.Count != SlotCount)
        {
            LogHelper.Warn(this, $"载具干完了，但 Load 时的槽图是 {loadedMap.Count} 槽、machine.xml 配的是 {SlotCount} 槽，没法对账");
            return;
        }

        // 从这个口出去、回到别的 LoadPort 上的片，按来处的槽号记下
        var returnedElsewhere = new HashSet<int>();
        foreach (var location in _ledger.Locations)
        {
            if (!_ledger.IsLoadPort(location.Module) || string.Equals(location.Module, Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var wafer in _ledger.GetSlots(location.Module))
            {
                if (wafer is not null && string.Equals(wafer.SourceLoadPort, Name, StringComparison.OrdinalIgnoreCase))
                {
                    returnedElsewhere.Add(wafer.SourceSlot);
                }
            }
        }

        var slots = _ledger.GetSlots(Name);
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
            LogHelper.Info(this, "载具干完了（EC AutoUnload 关着，不卸）：Load 时的槽图跟晶圆账一致");
            return;
        }

        LogHelper.Error(this, $"载具干完了，Load 时的槽图跟晶圆账对不上：{string.Join("；", mismatched)}");
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
    /// <param name="routine"></param>
    protected virtual void RaiseActionFailedAlarm(LoadPortAction action, ModuleRoutine routine)
    {
       
        if (routine.IsSuccess || routine.Code == ErrorCodes.Aborted)
        {
            return;
        }

        // 设备做完了、Mapping 不能用：报专门的一条，看报警就知道去查槽数、理片
        if (routine.Code == ErrorCodes.SlotMapLengthMismatch || routine.Code == ErrorCodes.SlotMapAbnormal
            || routine.Code == ErrorCodes.UnloadSlotMapMismatch)
        {
            RaiseAlarm(SlotMapAlarm);
            return;
        }

        // 不是超时：报动作失败，端口停在 Error，等人处理、复位
        if (routine.Code != ErrorCodes.Timeout)
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
    /// E87 上报放进 EAP 的派发元件
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
    /// E84 上报放进 EAP 的派发元件
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
