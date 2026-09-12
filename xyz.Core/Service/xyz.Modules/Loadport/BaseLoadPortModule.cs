using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Components;
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

public abstract class BaseLoadPortModule : BaseModule, ILoadPort
{
    #region SV 状态变量

    /// <summary>
    /// LoadPort 当前状态（SV）。既可以保存平台公共状态，也可以保存 LoadPort 专属状态。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "模块状态码")]
    public int State { get; protected set; } = ModuleState.NotInit;

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

    /// <summary>
    /// RFID 读头：基类只依赖 IRfidReader 接口，具体品牌组件由 CreateRfidReader 决定，机型可重写更换。
    /// </summary>
    public IRfidReader RFID { get; }

    #endregion


    protected BaseLoadPortModule()
    {
        var rfid = CreateRfidReader();
        RFID = rfid;
        AddChild(rfid);
    }

    /// <summary>
    /// 创建 RFID 读头组件；默认通用组件（只承载装机配置，不含品牌协议）。
    /// 机型使用不同 RFID 读头时重写，返回实现了 IRfidReader 的 RfidReaderComponent 子类。
    /// </summary>
    protected virtual RfidReaderComponent CreateRfidReader()
    {
        return new RfidReaderComponent();
    }

    #region 驱动连接

    /// <summary>
    /// 当前驱动；Open 之后可用。公开给机型操作类（LoadOperation 等）取用发指令。
    /// </summary>
    public LoadPortDriverBase? Driver { get; private set; }

    /// <summary>
    /// 按通讯类型建字节传输（SC 的 CommType 决定串口/网口）。
    /// </summary>
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

        if (!RFID.Open())
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
        RFID.Close();
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
    /// 在设备状态扫描后调用：首次发布，之后只在状态变化时发布。
    /// EventBus 留存最后一条消息，供界面晚订阅或重连时补发。
    /// </summary>
    protected void PublishState()
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

    /// <summary>
    /// 通过 LoadPort 内部的 RFID 组件读取载具 ID。
    /// </summary>
    public string? ReadCarrierId() => RFID.ReadCarrierId();

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
    /// 设置自动/手动模式（内部模式位，不经设备协议）；置位后由下一次扫描随状态事件发布。
    /// </summary>
    public void SetAutoMode(bool autoMode)
    {
        IsAutoMode = autoMode;
    }

    /// <summary>
    /// 发起动作：前置检查 + 状态迁移表 + 挂载传入的操作并进入执行状态，立即返回。
    /// 操作由模块扫描线程自动步进（见 BaseModule），终结按迁移表落状态。
    /// 返回操作实例；null 表示被拒绝：未启用、驱动未建、已有在途动作（Abort 除外）或状态表不允许。
    /// </summary>
    protected ModuleOperation? Begin(LoadPortAction action, ModuleOperation operation)
    {
        lock (OperationGate)
        {
            if (!IsEnable || Driver is null)
            {
                return null;
            }

            if (!LoadPortStateTable.TryGetTransition(State, action, out var transition))
            {
                return null;
            }

            if (!Run(operation, replace: action == LoadPortAction.Abort))
            {
                return null;
            }

            _transition = transition;
            State = transition.ExecutingState;
            return operation;
        }
    }

    /// <summary>
    /// 操作终结：成功落迁移表的成功态，失败/被打断落 Error。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        State = operation.IsSuccess ? _transition.SuccessState : ModuleState.Error;
    }

    #endregion
}
