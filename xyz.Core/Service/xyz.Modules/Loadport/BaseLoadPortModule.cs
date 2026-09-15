using System.Collections.Concurrent;
using xyz.Common.Log;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Drivers.Communication;
using xyz.Drivers.Communication.Serial;
using xyz.Drivers.Communication.Tcp;
using xyz.Drivers.Loadport;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Modules;

public abstract class BaseLoadPortModule : BaseTransferStationModule, ILoadPort
{
    #region SV 状态变量

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
    /// 自动/手动模式（SV）。内部控制位，不经设备协议：
    /// Online()/Offline() 置位；默认手动，掉电不保持。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "自动模式（true=自动，false=手动）")]
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

    #region SC 装机常量

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

    [SCEditor("1", "LoadPort", "工位号")]
    public int ModuleNumber { get; set; } = 1;

    [SCEditor("0", "LoadPort", "机械手服务此工位的平移位置")]
    public double TXPoint { get; set; }

    [SCEditor("T_South", "LoadPort", "机械手服务此工位的转盘方位")]
    public string TAction { get; set; } = "T_South";

    [SCEditor("1", "LoadPort", "机械手从本 LoadPort 取片用的手臂")]
    public int UseArm { get; set; } = 1;

    #endregion

    #region EC 在线参数（查询与动作超时，属性读写直通 EC，改完即时生效）

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

    #region Event 采集事件

    [EventAttribut("FOUP 到达", Description = "FOUP 从不在位变为在位")]
    public readonly string FoupArrivedEvent = "FoupArrived";

    [EventAttribut("FOUP 移除", Description = "FOUP 从在位变为不在位")]
    public readonly string FoupRemovedEvent = "FoupRemoved";

    #endregion

    #region Component

    public IRfidReader? RFID => FindChild<IRfidReader>();

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

    public LoadPortDriverBase? Driver { get; private set; }

    protected ICommunication CreateTransport()
    {
        return CommType switch
        {
            CommType.Serial => new SerialCommunication().Create(PortName, BaudRate, Parity, DataBits, StopBits),
            CommType.Tcp => new TcpCommunication().Create(Host, NetPort),
            _ => throw new NotSupportedException($"不支持的通讯类型: {CommType}"),
        };
    }

    /// <summary>
    /// 创建品牌驱动（传输 + 品牌帧编解码 + 驱动）；机型模块重写。
    /// </summary>
    protected virtual LoadPortDriverBase CreateDriver()
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

        Driver = CreateDriver();
        Driver.OnSpontaneousEvent += OnDriverSpontaneousEvent;  //事件
        return Driver.Open();
    }

    /// <summary>
    /// 关闭 RFID 读头与驱动连接；与 Open 成对，宿主退出时调用（当前宿主常驻，暂无调用点）。
    /// </summary>
    public void Close()
    {
        RFID?.Close();
        Driver?.Close();
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
    /// 发布当前状态（扫描周期与传片环标记都会调）：首次发布，之后只在状态变化时发布。
    /// EventBus 留存最后一条消息，供界面晚订阅或重连时补发。
    /// </summary>
    protected override void PublishState()
    {
        var dto = new LoadPortDto
        {
            Name = Name,
            State = State,
            IsPodPlaced = IsPodPlaced,
            AutoMode = IsAutoMode,
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
    /// 通过 LoadPort 内部的 RFID 组件读取载具 ID；未挂载读头组件时返回 null（不回调 EAP）。
    /// 读到非空 ID：更新 CarrierId 并回调 CarrierIdRead；读头返回空或抛异常：回调 CarrierIdReadFailed（异常原样抛出）。
    /// </summary>
    public string? ReadCarrierId()
    {
        var reader = RFID;
        if (reader is null)
        {
            return null;
        }

        string? result;
        try
        {
            result = reader.ReadCarrierId();
        }
        catch
        {
            EnqueueEap(callback => callback.CarrierIdReadFailed(this));
            throw;
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            EnqueueEap(callback => callback.CarrierIdReadFailed(this));
            return null;
        }

        string carrierId = result;
        CarrierId = carrierId;
        EnqueueEap(callback => callback.CarrierIdRead(this, carrierId));
        return carrierId;
    }

    /// <summary>
    /// 改写载具 ID：Host 确认的 ID 与读到的不一致时以 Host 为准（对应 CTC 的 ProceedSetCarrierID）；不回调 EAP。
    /// </summary>
    public void SetCarrierId(string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        CarrierId = carrierId;
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
        EnqueueEap(callback => callback.SlotMapRead(this, snapshot));
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
    /// 设置自动/手动模式（内部模式位，不经设备协议）；置位后由下一次扫描随状态事件发布。
    /// 模式有变化时回调 EAP AutoModeChanged。
    /// </summary>
    public void SetAutoMode(bool autoMode)
    {
        if (IsAutoMode == autoMode)
        {
            return;
        }

        IsAutoMode = autoMode;
        EnqueueEap(callback => callback.AutoModeChanged(this, autoMode));
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
    /// Load/Unload/Home/Clamp/Unclamp 成功时回调 EAP（在模块锁内只入队，派发在扫描线程锁外进行）。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        State = operation.IsSuccess ? _transition.SuccessState : ModuleState.Error;

        if (!operation.IsSuccess)
        {
            return;
        }

        switch (_action)
        {
            case LoadPortAction.Load:
                EnqueueEap(callback => callback.LoadCompleted(this));
                break;

            case LoadPortAction.Unload:
                EnqueueEap(callback => callback.UnloadCompleted(this));
                break;

            case LoadPortAction.Home:
                EnqueueEap(callback => callback.Homed(this));
                break;

            case LoadPortAction.Clamp:
                EnqueueEap(callback => callback.ClampCompleted(this));
                break;

            case LoadPortAction.Unclamp:
                EnqueueEap(callback => callback.UnclampCompleted(this));
                break;
        }
    }

    #endregion

    #region EAP 口子（对应 CTC 的 LPCallBack：载具节点回调给 EAP，EAP 经 ILoadPort 反向下发动作）

    private readonly ConcurrentQueue<Action<ILoadPortEapCallback>> _eapNotifications = new();
    private bool _lastPodPlaced;

    /// <summary>
    /// EAP 回调；null 表示未接 EAP，模块照常运行。装配时由 EAP 侧挂上。
    /// </summary>
    public ILoadPortEapCallback? EapCallback { get; set; }

    /// <summary>
    /// 扫描周期：先扫子组件与操作（基类），再判载具在位边沿，最后派发 EAP 回调。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        CheckCarrierPresence();
        DispatchEapNotifications();
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
            EnqueueEap(callback => callback.CarrierArrived(this));
            return;
        }

        string? carrierId = CarrierId;
        CarrierId = null;
        SlotMap = Array.Empty<SlotState>();
        EnqueueEap(callback => callback.CarrierRemoved(this, carrierId));
    }

    /// <summary>
    /// 入队一条 EAP 回调；未挂 EAP 时直接丢弃。任意线程可调。
    /// </summary>
    private void EnqueueEap(Action<ILoadPortEapCallback> notification)
    {
        if (EapCallback is null)
        {
            return;
        }

        _eapNotifications.Enqueue(notification);
    }

    /// <summary>
    /// 在扫描线程上按入队顺序派发，不持有模块锁（回调里可直接调 ILoadPort 动作）；单条异常只记日志。
    /// </summary>
    private void DispatchEapNotifications()
    {
        while (_eapNotifications.TryDequeue(out var notification))
        {
            var callback = EapCallback;
            if (callback is null)
            {
                continue;
            }

            try
            {
                notification(callback);
            }
            catch (Exception exception)
            {
                LogHelper.Warn(Name, $"EAP 回调异常: {exception.Message}");
            }
        }
    }

    #endregion
}
