using System.Collections.Concurrent;
using System.Globalization;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Configs.Models;
using xyz.Drivers.Communication;
using xyz.Drivers.Communication.Tcp;
using xyz.Drivers.Robot;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Modules;

public abstract class BaseRobotModule : BaseModule, IRobot
{
    #region SV 状态变量

    /// <summary>
    /// Robot 当前状态（SV）。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "模块状态码")]
    public int State { get; protected set; } = ModuleState.NotInit;

    /// <summary>
    /// 伺服是否上使能（SV）：机型轮询查询刷新；尚未查到为 null。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "伺服是否上使能")]
    public bool? IsServoOn { get; protected set; }

    private volatile string? _deviceError;

    /// <summary>
    /// 设备当前报错（SV，错误码#内容）：机型轮询查询或设备主动推送刷新；无报错为 null。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "设备当前报错")]
    public string? DeviceError
    {
        get => _deviceError;
        protected set => _deviceError = value;
    }

    private readonly ConcurrentDictionary<int, bool> _armWafers = new();

    /// <summary>
    /// 手指上是否有片：驱动手指在位主动推送刷新；尚未收到该手指的推送为 null。
    /// </summary>
    public bool? HasWafer(int arm)
    {
        return _armWafers.TryGetValue(arm, out bool hasWafer) ? hasWafer : null;
    }

    #endregion

    #region SC 装机常量

    [SCEditor("", "Robot", "Robot 品牌")]
    public string Brand { get; set; } = string.Empty;

    [SCEditor("True", "Robot", "是否启用本 Robot (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("127.0.0.1", "Robot", "Robot 网口 IP")]
    public string Host { get; set; } = "127.0.0.1";

    [SCEditor("9000", "Robot", "Robot 网口端口")]
    public int NetPort { get; set; } = 9000;

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
        @default: "60000", description: "Home 动作超时（全轴回原点）")]
    public int HomeTimeout
    {
        get { return GetEcInt(nameof(HomeTimeout)); }
        set { SetEcInt(nameof(HomeTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "60000", description: "Pick 动作超时（取片）")]
    public int PickTimeout
    {
        get { return GetEcInt(nameof(PickTimeout)); }
        set { SetEcInt(nameof(PickTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "60000", description: "Place 动作超时（放片）")]
    public int PlaceTimeout
    {
        get { return GetEcInt(nameof(PlaceTimeout)); }
        set { SetEcInt(nameof(PlaceTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "10000", description: "Reset 动作超时（清错）")]
    public int ResetTimeout
    {
        get { return GetEcInt(nameof(ResetTimeout)); }
        set { SetEcInt(nameof(ResetTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "10000", description: "Abort 动作超时（急停）")]
    public int AbortTimeout
    {
        get { return GetEcInt(nameof(AbortTimeout)); }
        set { SetEcInt(nameof(AbortTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "10000", description: "PowerOn/PowerOff 动作超时（上/下使能）")]
    public int PowerTimeout
    {
        get { return GetEcInt(nameof(PowerTimeout)); }
        set { SetEcInt(nameof(PowerTimeout), value); }
    }

    #endregion

    #region 站点表（SC：本 Robot 节点下的 Stations 子节点，每个 Value 为 模块名 = 站点号）

    private const string StationsSettingName = "Stations";

    private IReadOnlyDictionary<string, int> _stations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 本机械手的站点表：模块名（如 LoadPort1）→ 设备站点号，取放片按它下发。
    /// 每台机械手各配各的，同一模块在不同机械手上的站点号可以不同；装配时读入，运行中不变。
    /// </summary>
    public IReadOnlyDictionary<string, int> Stations => _stations;

    /// <summary>
    /// 按模块名查站点号（忽略大小写）；未配置返回 false。
    /// </summary>
    public bool TryGetStation(string station, out int stationNumber)
    {
        return _stations.TryGetValue(station, out stationNumber);
    }

    /// <summary>
    /// 装配时读入站点表；站点号不是正整数或模块名重复时抛异常，装配即失败。
    /// </summary>
    protected override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        var stations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var node = setting.Children.FirstOrDefault(child =>
            string.Equals(child.Name, StationsSettingName, StringComparison.OrdinalIgnoreCase));
        if (node is not null)
        {
            foreach (var value in node.Values)
            {
                if (!int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                    || number < 1)
                {
                    throw new InvalidOperationException(
                        $"sc.xml 节点 {setting.Name}.{StationsSettingName} 的站点 {value.Name}=\"{value.Value}\" 不是有效站点号（须为正整数）。");
                }

                if (!stations.TryAdd(value.Name, number))
                {
                    throw new InvalidOperationException(
                        $"sc.xml 节点 {setting.Name}.{StationsSettingName} 的站点 {value.Name} 重复配置。");
                }
            }
        }

        _stations = stations;
    }

    #endregion

    #region Alarm

    [Alarm("Robot 设备报警", AlarmCategory.HardwareError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "Robot 控制器上报报错",
        Solution = "查询报错内容，排除故障后复位并重新回原点")]
    public string RobotDeviceAlarm = nameof(RobotDeviceAlarm);

    #endregion

    protected BaseRobotModule()
    {
        RegisterTransitions(RobotStateTable.ToModuleTable());
    }

    #region 驱动连接

    public RobotDriverBase? Driver { get; private set; }

    /// <summary>
    /// Robot 走网口（TCP）。
    /// </summary>
    protected ICommunication CreateTransport()
    {
        return new TcpCommunication().Create(Host, NetPort);
    }

    /// <summary>
    /// 创建品牌驱动（传输 + 品牌帧编解码 + 驱动）；机型模块重写。
    /// </summary>
    protected virtual RobotDriverBase CreateDriver()
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

        Driver = CreateDriver();
        Driver.OnSpontaneousEvent += OnDriverSpontaneousEvent;
        return Driver.Open();
    }

    /// <summary>
    /// 关闭驱动连接；与 Open 成对，宿主退出时调用（当前宿主常驻，暂无调用点）。
    /// </summary>
    public void Close()
    {
        Driver?.Close();
    }

    private void OnDriverSpontaneousEvent(RobotDeviceEvent evt)
    {
        // 在驱动路由线程回调，只做轻量状态翻转。
        switch (evt.Kind)
        {
            case RobotDeviceEventKind.WaferPresence:
                _armWafers[evt.Arm] = evt.HasWafer;
                break;

            case RobotDeviceEventKind.DeviceError:
                DeviceError = evt.Content;
                break;
        }
    }

    #endregion

    #region 状态发布

    private RobotDto? _lastPublishedState;

    /// <summary>
    /// 当前状态快照，状态发布与 GetState 查询共用。
    /// 未连接或停用时查询反馈（伺服使能、设备报错）不可信，置 null；手指在位保留最后一次推送值。
    /// </summary>
    public RobotDto CreateStateDto()
    {
        var dto = new RobotDto
        {
            Name = Name,
            State = State,
            Arms = _armWafers
                .OrderBy(pair => pair.Key)
                .Select(pair => new RobotArmDto { Arm = pair.Key, HasWafer = pair.Value })
                .ToList(),
        };

        var driver = Driver;
        if (driver is not null)
        {
            dto.IsConnected = driver.IsConnected;
        }

        if (dto.IsConnected && IsEnable)
        {
            dto.IsServoOn = IsServoOn;
            dto.DeviceError = DeviceError;
        }

        return dto;
    }

    /// <summary>
    /// 发布当前状态（扫描周期调用）：首次发布，之后只在状态变化时发布。
    /// EventBus 留存最后一条消息，供界面晚订阅或重连时补发。
    /// </summary>
    protected virtual void PublishState()
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

    #region Action（IRobot 契约：动作体由机型实现——直接创建操作）

    private (int ExecutingState, int SuccessState) _transition;

    /// <summary>
    /// 发起 Home。机型实现：Begin(RobotAction.Home, new ...Operation(...))。
    /// </summary>
    public abstract ModuleOperation? Home();

    /// <summary>
    /// 发起 Reset（清设备报错）。
    /// </summary>
    public abstract ModuleOperation? Reset();

    /// <summary>
    /// 发起 Abort（急停）；Abort 可顶替在途动作。
    /// </summary>
    public abstract ModuleOperation? Abort();

    /// <summary>
    /// 发起 Pick：station 为站点表中的模块名（如 LoadPort1），站点号取本机械手站点表；未配置的站点被拒（返回 null）。
    /// </summary>
    public ModuleOperation? Pick(int arm, string station, int slot)
    {
        return TryGetStation(station, out int stationNumber) ? Pick(arm, stationNumber, slot) : null;
    }

    /// <summary>
    /// 发起 Place：station 为站点表中的模块名，站点号取本机械手站点表；未配置的站点被拒（返回 null）。
    /// </summary>
    public ModuleOperation? Place(int arm, string station, int slot)
    {
        return TryGetStation(station, out int stationNumber) ? Place(arm, stationNumber, slot) : null;
    }

    /// <summary>
    /// 按设备站点号发起 Pick（站点号已由站点表解析）。机型实现：Begin(RobotAction.Pick, new ...Operation(...))。
    /// </summary>
    protected abstract ModuleOperation? Pick(int arm, int stationNumber, int slot);

    /// <summary>
    /// 按设备站点号发起 Place（站点号已由站点表解析）。
    /// </summary>
    protected abstract ModuleOperation? Place(int arm, int stationNumber, int slot);

    /// <summary>
    /// 发起 PowerOn（伺服上使能）。
    /// </summary>
    public abstract ModuleOperation? PowerOn();

    /// <summary>
    /// 发起 PowerOff（伺服下使能）。
    /// </summary>
    public abstract ModuleOperation? PowerOff();

    protected ModuleOperation? Begin(RobotAction action, ModuleOperation operation)
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

            if (!Run(operation, replace: action == RobotAction.Abort))
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
