using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using xyz.Common.Log;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Configs.Models;
using xyz.Drivers.Robot;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

public abstract class BaseRobotModule : BaseModule, IRobot
{
    #region SV 

    [VariableMark(VariableType.SV, ValueFormat.Int, description: "模块状态码")]
    public override int State { get; protected set; } = ModuleState.NotInit;

    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "伺服是否上使能")]
    public bool? IsServoOn { get; protected set; }

    private volatile string? _deviceError;

    [VariableMark(VariableType.SV, ValueFormat.String, description: "设备当前报错")]
    public string? DeviceError
    {
        get => _deviceError;
        protected set => _deviceError = value;
    }

    #endregion

    private readonly ConcurrentDictionary<int, bool> _armWafers = new();

    private readonly ConcurrentDictionary<string, double> _axisPositions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 手指上是否有片：驱动手指在位主动推送刷新；尚未收到该手指的推送为 null。
    /// </summary>
    public bool? HasWafer(int arm)
    {
        return _armWafers.TryGetValue(arm, out bool hasWafer) ? hasWafer : null;
    }

    /// <summary>
    /// 记下轴坐标。扫描查询回包时调，只写缓存不做重活。
    /// </summary>
    protected void NoteAxisPos(string axis, double position)
    {
        _axisPositions[axis] = position;
    }




    #region SC

    [SCEditor("True", "Robot", "是否启用本 Robot (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    // 品牌/网口/轴表在 Driver 子组件上配（RobotDriverComponent）：换品牌就是换那个节点的 Type。

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

    #region Alarm

    [Alarm("Robot 设备报警", AlarmCategory.HardwareError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "Robot 控制器上报报错",
        Solution = "查询报错内容，排除故障后复位并重新回原点")]
    public string RobotDeviceAlarm = nameof(RobotDeviceAlarm);

    [Alarm("Robot 受控停止", AlarmCategory.MotionError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "取放片、回原点等动作失败或超时，Robot 进入错误状态",
        Solution = "先确认手指与站点上的实际片位，再复位并重新回原点")]
    public string ControlledStopAlarm = nameof(ControlledStopAlarm);

    #endregion

    #region 站点表

    private IReadOnlyDictionary<string, RobotStation> _stations = new Dictionary<string, RobotStation>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, RobotStation> Stations => _stations;
    private volatile RobotStation? _currentStation;

    public bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config)
    {
        return _stations.TryGetValue(station, out config);
    }

    protected override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        var stations = new Dictionary<string, RobotStation>(StringComparer.OrdinalIgnoreCase);
        var node = setting.Children.FirstOrDefault(child =>
            string.Equals(child.Name, "Stations", StringComparison.OrdinalIgnoreCase));

        foreach (var stationNode in node?.Children ?? [])
        {
            string path = $"{setting.Name}.Stations.{stationNode.Name}";
            if (!stations.TryAdd(stationNode.Name, RobotStation.FromConfig(stationNode, path)))
            {
                throw new InvalidOperationException($"sc.xml 节点 {path} 重复配置。");
            }
        }

        _stations = stations;
    }

    #endregion
    protected BaseRobotModule()
    {
        RegisterTransitions(RobotStateTable.ToModuleTable());
    }

    #region 驱动连接

    /// <summary>
    /// 品牌驱动组件（sc.xml 本模块下的 Driver 子节点）：换 Type 即换品牌。
    /// 打开成功后有值；装机停用或没挂驱动组件时为 null。
    /// </summary>
    public RobotDriverComponent? Robot { get; private set; }

    /// <summary>
    /// 手指数：驱动组件轴表里 Arm* 的个数（手指号从 1 开始；晶圆账按手指注册槽位）。
    /// </summary>
    public int ArmCount
    {
        get { return Robot?.ArmCount ?? 0; }
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

        var robot = FindChild<RobotDriverComponent>();
        if (robot is null)
        {
            LogHelper.Error(Name, "sc.xml 没挂驱动组件：本模块下要有 Driver 子节点（Type 指定品牌壳）");
            return false;
        }

        Robot = robot;

        // 先摘后挂：Open 可能不止一次（重开），保证只挂一份。
        robot.DeviceEvent -= OnDeviceEvent;
        robot.DeviceEvent += OnDeviceEvent;

        // 手指在晶圆账里也是槽位：片停在手上算在途，跟停在花篮里一样要有位置。
        WaferManager.Current?.RegisterLocation(Name, robot.ArmCount);
        return robot.Open();
    }

    /// <summary>
    /// 关闭驱动连接；与 Open 成对，宿主退出时调用（当前宿主常驻，暂无调用点）。
    /// </summary>
    public void Close()
    {
        Robot?.Close();
    }

    /// <summary>
    /// 设备主动推送（驱动路由线程上回调，事件已归一成 RobotDeviceEvent）：只做轻量状态翻转。
    /// </summary>
    private void OnDeviceEvent(RobotDeviceEvent evt)
    {
        switch (evt.Kind)
        {
            case RobotDeviceEventKind.WaferPresence:
                NoteWaferPresence(evt.Arm, evt.HasWafer);
                break;

            case RobotDeviceEventKind.DeviceError:
                DeviceError = evt.Content;
                break;
        }
    }

    /// <summary>
    /// 记下手指在位。在驱动路由线程上调，只翻标志不做重活。
    /// </summary>
    protected void NoteWaferPresence(int arm, bool hasWafer)
    {
        _armWafers[arm] = hasWafer;
    }

    #endregion

    #region 状态发布

    private RobotDto? _lastPublishedState;

    /// <summary>
    /// 当前状态快照，状态发布与 GetState 查询共用。
    /// 未连接或停用时查询反馈（伺服使能、设备报错）不可信，置 null；手指在位保留最后一次推送值；
    /// 当前站点及其伸出方向、伸出距离 Y 取最近一次发起成功的取放片，还没取放过为 北 / 0；
    /// 站点表与轴坐标整表下推，界面（站点下拉、轴位表）不写死。
    /// </summary>
    public RobotDto CreateStateDto()
    {
        var station = _currentStation;
        var dto = new RobotDto
        {
            Name = Name,
            State = State,
            Mode = Mode,
            Station = station?.Name,
            Direction = station?.Direction ?? RobotDirection.North,
            Y = station?.Y ?? 0,
            Stations = _stations.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
            StationInfos = _stations.Values
                .OrderBy(station => station.Number)
                .Select(station => new RobotStationDto
                {
                    Name = station.Name,
                    Number = station.Number,
                    Direction = station.Direction,
                    Y = station.Y,
                })
                .ToList(),
            Arms = _armWafers
                .OrderBy(pair => pair.Key)
                .Select(pair => new RobotArmDto { Arm = pair.Key, HasWafer = pair.Value })
                .ToList(),
        };

        var robot = Robot;
        if (robot is not null)
        {
            dto.IsConnected = robot.IsConnected;

            // 轴坐标按轴表顺序，还没查到的轴不下发。
            foreach (var axis in robot.AxisList)
            {
                if (_axisPositions.TryGetValue(axis, out double position))
                {
                    dto.AxisPositions.Add(new RobotAxisPositionDto { Name = axis, Position = position });
                }
            }
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

    #region Action（IRobot 契约：动作体由机型实现——直接创建操作）

    /// <summary>
    /// 发起 Home。机型实现：Begin(RobotAction.Home, new ...Operation(...))。
    /// </summary>
    public abstract ModuleOperation? Home();

    /// <summary>
    /// 初始化（重写组件基类的 Init）：先初始化子组件，再回原点——Home 就是机械手的初始化（NotInit → Idle）。
    /// 返回 Home 操作，调用方等它做完；状态不允许时为 null。
    /// </summary>
    public override ModuleOperation? Init()
    {
        base.Init();
        return Home();
    }

    /// <summary>
    /// 复位（重写组件基类的 Reset）：先清报警、复位子组件，再发设备复位清错。
    /// 返回设备复位操作，调用方等它做完；状态不允许时为 null，报警照样已经清了。
    /// </summary>
    public override ModuleOperation? Reset()
    {
        base.Reset();
        return ResetDevice();
    }

    /// <summary>
    /// 发设备复位清错。机型实现：Begin(RobotAction.Reset, new ...Operation(...))。
    /// </summary>
    protected abstract ModuleOperation? ResetDevice();

    /// <summary>
    /// 中止（重写组件基类的 Abort，急停）：先中止子组件，再发设备中止；Abort 可顶替在途动作，不清报警。
    /// 返回设备中止操作；状态不允许时为 null。
    /// </summary>
    public override ModuleOperation? Abort()
    {
        base.Abort();
        return AbortDevice();
    }

    /// <summary>
    /// 发设备中止。机型实现：Begin(RobotAction.Abort, new ...Operation(...))。
    /// </summary>
    protected abstract ModuleOperation? AbortDevice();

    /// <summary>
    /// 发起 Pick：station 为站点表中的模块名（如 LoadPort1），站点号取本机械手站点表；未配置的站点被拒（返回 null）。
    /// </summary>
    public ModuleOperation? Pick(int arm, string station, int slot)
    {
        return BeginAtStation(RobotAction.Pick, arm, station, slot,
            config => Begin(RobotAction.Pick, CreatePickOperation(arm, config.Number, slot)));
    }

    /// <summary>
    /// 发起 Place：station 为站点表中的模块名，站点号取本机械手站点表；未配置的站点被拒（返回 null）。
    /// </summary>
    public ModuleOperation? Place(int arm, string station, int slot)
    {
        return BeginAtStation(RobotAction.Place, arm, station, slot,
            config => Begin(RobotAction.Place, CreatePlaceOperation(arm, config.Number, slot)));
    }

    /// <summary>
    /// 查站点表并发起取放片；发起成功才记为当前站点（被拒不改），随状态推给界面显示机械手去哪、朝哪。
    /// </summary>
    private ModuleOperation? BeginAtStation(
        RobotAction action, int arm, string station, int slot, Func<RobotStation, ModuleOperation?> begin)
    {
        if (!TryGetStation(station, out var config))
        {
            return null;
        }

        // 挂操作和记意图必须在同一把锁里：否则扫描线程可能在意图记上之前就把操作终结了，这一趟就不记账。
        lock (OperationGate)
        {
            var operation = begin(config);
            if (operation is null)
            {
                return null;
            }

            _currentStation = config;
            _intent = new TransferIntent(action, arm, station, slot);
            return operation;
        }
    }

    /// <summary>
    /// 造一个取片操作（站点号已由站点表解析成设备站点号）；发不发得出去由基类查表决定。
    /// </summary>
    protected abstract ModuleOperation CreatePickOperation(int arm, int stationNumber, int slot);

    /// <summary>
    /// 造一个放片操作。
    /// </summary>
    protected abstract ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot);

    /// <summary>
    /// 发起 PowerOn（伺服上使能）。
    /// </summary>
    public abstract ModuleOperation? PowerOn();

    /// <summary>
    /// 发起 PowerOff（伺服下使能）。
    /// </summary>
    public abstract ModuleOperation? PowerOff();

    /// <summary>
    /// 装机停用、或驱动组件没挂起来（装配里 Open 失败）都不发动作。
    /// </summary>
    protected override bool CanBeginAction => IsEnable && Robot is not null;

    /// <summary>
    /// 操作终结（状态已由基类落好）：记晶圆账，失败的动作报警。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        UpdateLedger(operation);
        UpdateActionAlarms(operation);
    }

    #endregion

    #region 报警

    /// <summary>
    /// 扫描周期：先扫子组件与操作（基类），再按设备报错刷新报警。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        CheckDeviceAlarm();
    }

    /// <summary>
    /// 设备报警跟着设备报错走：有报错就报。报错没了也不清——报警只能人工 Reset 清。
    /// 没连上时 DeviceError 是上一次的旧值，不作数，不判。
    /// </summary>
    private void CheckDeviceAlarm()
    {
        var robot = Robot;
        if (robot is not null && robot.IsConnected && !string.IsNullOrEmpty(DeviceError))
        {
            RaiseAlarm(RobotDeviceAlarm);
        }
    }

    /// <summary>
    /// 动作类报警：失败就报；动作成功也不清，只能人工 Reset 清。
    /// 人为急停顶掉的动作不报——那是操作员自己按的，不是故障。
    /// </summary>
    private void UpdateActionAlarms(ModuleOperation operation)
    {
        if (!operation.IsSuccess && operation.Code != ErrorCodes.Aborted)
        {
            RaiseAlarm(ControlledStopAlarm);
        }
    }

    #endregion

    #region 晶圆账

    /// <summary>本次取放的意图；非取放动作为 null。</summary>
    private sealed record TransferIntent(RobotAction Action, int Arm, string Station, int Slot);

    private TransferIntent? _intent;

    /// <summary>
    /// 取放成功后写账：Pick 把片从站点槽位移到手指上，Place 反过来。
    /// 失败或被打断不动账——片到底在手上还是在槽里已经说不准了，乱改账比不改更糟，留给人工对账。
    /// 在模块锁内调用（OnOperationCompleted 本身就在锁里）。
    /// </summary>
    private void UpdateLedger(ModuleOperation operation)
    {
        var intent = _intent;
        _intent = null;
        if (intent is null || !operation.IsSuccess)
        {
            return;
        }

        var ledger = WaferManager.Current;
        if (ledger is null)
        {
            return;
        }

        bool moved = intent.Action == RobotAction.Pick
            ? ledger.Move(intent.Station, intent.Slot, Name, intent.Arm)
            : ledger.Move(Name, intent.Arm, intent.Station, intent.Slot);

        // 设备说取放成功，账却移不动（源上没片/目标已有片）：账实不符。
        // 账本自己已经报警了，这里补一条带动作的日志，方便现场对着流水查。
        if (!moved)
        {
            LogHelper.Error(Name,
                $"{intent.Action} 成功但账没记上：{intent.Station}.{intent.Slot:00} ↔ {Name}.{intent.Arm:00}");
        }
    }

    #endregion
}
