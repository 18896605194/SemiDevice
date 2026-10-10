using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using xyz.Common.Log;
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
    public override int State { get; protected set; } = RobotState.NotInit;

    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "伺服是否上使能")]
    public bool? IsServoOn { get; protected set; }

    [VariableMark(VariableType.SV, ValueFormat.Double, unit: "%", description: "全局速度百分比")]
    public double? Speed { get; protected set; }

    private volatile string? _deviceError;

    [VariableMark(VariableType.SV, ValueFormat.String, description: "设备当前报错")]
    public string? DeviceError
    {
        get => _deviceError;
        protected set => _deviceError = value;
    }

    #endregion

    #region SC

    [SCEditor("True", "_robot", "是否启用本 _robot (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

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

    [Alarm("_robot 设备报警", AlarmCategory.HardwareError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "_robot 控制器上报报错",
        Solution = "查询报错内容，排除故障后复位并重新回原点")]
    public string RobotDeviceAlarm = nameof(RobotDeviceAlarm);

    [Alarm("_robot 受控停止", AlarmCategory.MotionError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "取放片、回原点等动作失败或超时，_robot 进入错误状态",
        Solution = "先确认手指与站点上的实际片位，再复位并重新回原点")]
    public string ControlledStopAlarm = nameof(ControlledStopAlarm);

    #endregion

    #region Component

    /// <summary>
    /// 品牌驱动组件（sc.xml 本模块下的 Driver 子节点）：换 Type 即换品牌。
    /// 打开成功后有值；装机停用或没挂驱动组件时为 null。
    /// </summary>
    public RobotDriverComponent? _robot { get; private set; }

    private IReadOnlyList<RobotAxisComponent>? _axes;
    private IReadOnlyList<RobotArmComponent>? _arms;

    /// <summary>
    /// 本机械手的轴组件：直接查下面的子组件，sc.xml 轴节点的先后就是轴表顺序（查询轮询、界面轴位表都用它）。
    /// 手指（Arm1/Arm2）本身也是轴，在表里。
    /// </summary>
    public IReadOnlyList<RobotAxisComponent> Axes => _axes ??= [.. FindChildren<RobotAxisComponent>()];

    /// <summary>
    /// 手指组件，直接查下面的子组件：按手指号升序；晶圆账按它注册槽位（槽号 = 手指号）。
    /// </summary>
    public IReadOnlyList<RobotArmComponent> Arms => _arms ??= [.. FindChildren<RobotArmComponent>().OrderBy(arm => arm.Number)];

    /// <summary>
    /// 手指数：sc.xml 里 RobotArmComponent 节点的个数。
    /// </summary>
    public int ArmCount => Arms.Count;

    /// <summary>按手指号找手；没配返回 null。</summary>
    private RobotArmComponent? FindArm(int number) => Arms.FirstOrDefault(arm => arm.Number == number);

    #endregion

    #region 轴位查询（转给轴组件）

    /// <summary>
    /// 记下轴坐标。扫描查询回包时调：按轴名写进轴组件，只写缓存不做重活。
    /// </summary>
    protected void NoteAxisPos(string axis, double position)
    {
        foreach (var part in _axes)
        {
            if (string.Equals(part.Name, axis, StringComparison.OrdinalIgnoreCase))
            {
                part.NotePosition(position);
                return;
            }
        }

        LogHelper.Warn(Name, $"轴 {axis} 不在 sc.xml 轴节点里，坐标被忽略");
    }

    #endregion

    #region 站点表

    private IReadOnlyDictionary<string, RobotStation> _stations = new Dictionary<string, RobotStation>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, RobotStation> Stations => _stations;
    private volatile RobotStation? _currentStation;

    public bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config)
    {
        return _stations.TryGetValue(station, out config);
    }

    protected internal override void OnSettingLoaded(ModuleConfig setting)
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

    #region 组件初始化与驱动连接

    /// <summary>
    /// 组件初始化（开机，由装配在 Start 之前调用）：先把手指绑到晶圆账位置、注册手指的晶圆账槽位、订阅驱动的主动事件，
    /// 然后基类把子组件（驱动、轴、手指、传感器）各自初始化，驱动连接写在它自己的 InitComponent 里，模块只挂事件。
    /// 装机停用（IsEnable=False）的模块视为成功，空转；没挂驱动组件开机日志报错，子组件照样初始化，返回 false。
    /// </summary>
    public override bool InitComponent()
    {
        // 手指在晶圆账里也是槽位：片停在手上算在途，跟停在花篮里一样要有位置（槽号 = 手指号，所以要 1..N 连续）。
        var arms = Arms;
        for (int index = 0; index < arms.Count; index++)
        {
            var arm = arms[index];
            if (arm.Number != index + 1)
            {
                throw new InvalidOperationException(
                    $"sc.xml 节点 {arm.FullPath} 的手指号 {arm.Number} 不合法：要唯一、从 1 连续到 {arms.Count}（晶圆账槽位 = 手指号）。");
            }

            arm.BindLedger(Name);
        }

        if (Axes.Count == 0)
        {
            LogHelper.Error(Name, "sc.xml 没配轴节点：本模块下要有 X / Z / Theta / Arm* 这些 RobotAxisComponent / RobotArmComponent 节点");
        }

        // 站点表里配的手指必须存在，界面不会给出一只用不了的手。
        foreach (var station in _stations.Values)
        {
            foreach (int arm in station.Arms)
            {
                if (FindArm(arm) is null)
                {
                    LogHelper.Error(Name, $"sc.xml 站点 {station.Name} 的 Arms 配了 Arm{arm}，但没配这个手指节点");
                }
            }
        }

        if (!IsEnable)
        {
            return true;
        }

        var robot = FindChild<RobotDriverComponent>();
        if (robot is not null)
        {
            // 先摘后挂：初始化可重入，保证只挂一份；先挂再连，连上就可能有在位主动推送。
            robot.DeviceEvent -= OnDeviceEvent;
            robot.DeviceEvent += OnDeviceEvent;
            _robot = robot;
        }
        else
        {
            LogHelper.Error(Name, "sc.xml 没挂驱动组件：本模块下要有 Driver 子节点（Type 指定品牌壳）");
        }

        WaferManagerComponent.Current?.RegisterLocation(Name, ArmCount);

        bool childrenInitialized = base.InitComponent();
        return robot is not null && childrenInitialized;
    }

    /// <summary>
    /// 关闭驱动连接；与组件初始化成对，宿主退出时调用（当前宿主常驻，暂无调用点）。
    /// </summary>
    public void Close()
    {
        _robot?.Close();
    }

    /// <summary>
    /// 设备主动推送（驱动路由线程上回调，事件已归一成 RobotDeviceEvent）：只做轻量状态翻转。
    /// </summary>
    private void OnDeviceEvent(RobotDeviceEvent evt)
    {
        switch (evt.Kind)
        {
            case RobotDeviceEventKind.WaferPresence:
                RobotArmComponent? arm = FindArm(evt.Arm);
                if (arm is not null)
                {
                    arm.NoteWaferPresence(evt.HasWafer);
                }
                else
                {
                    // 设备推了 sc.xml 里没配的手指：不凭空长一只手，记日志忽略。
                    LogHelper.Warn(Name, $"收到手指 {evt.Arm} 的在位推送，但没配这个手指节点");
                }

                break;

                break;

            case RobotDeviceEventKind.DeviceError:
                DeviceError = evt.Content;
                break;
        }
    }

    #endregion

    #region 设备状态查询

    /// <summary>
    /// 手指在位推送未订阅成功时，每隔多少次查询重试一次订阅（首次查询即订阅）。
    /// </summary>
    private const int SubscribeRetryInterval = 20;

    private RobotCommand? _queryCommand;
    private QueryKind _queryKind;
    private int _queryCount;
    private bool _waferEventSubscribed;
    private string? _queryAxis;
    private int _axisScan;
    private readonly Stopwatch _queryWatch = new();

    /// <summary>超时 / 恢复只记一次日志：true = 上一次查询超时后还没查到（查到一次就清）。</summary>
    private bool _isStatusQueryLate;

    /// <summary>当前这条只读查询问的是什么；回包按它落模块状态，不认品牌指令类型。</summary>
    private enum QueryKind
    {
        SubscribeWaferEvent,
        ServoOn,
        DeviceError,
        Speed,
        AxisPos,
    }

    /// <summary>
    /// 设备状态轮询：先订阅手指在位推送，之后轮流查设备报错、伺服使能、速度与轴位；
    /// 一条没回就作废这一条、下一拍重发，超时 / 恢复各记一次日志。
    /// </summary>
    private void LoopQueryStatus()
    {
        if (!IsEnable)
        {
            return;
        }

        var robot = _robot;
        if (robot is null || !robot.IsConnected)
        {
            return;
        }

        #region 发查询

        // 手上没有在途的查询就发一条；没得查（轴表空）或驱动正忙被拒是 null，下一拍再发。
        if (_queryCommand is null)
        {
            _queryCommand = CreateQueryCommand(robot);
            if (_queryCommand is not null)
            {
                _queryWatch.Restart();
            }

            return;
        }

        #endregion

        #region 收结果

        if (_queryCommand.IsCompleted)
        {
            ApplyQueryResponse(_queryCommand);
            _queryCommand = null;
            _queryWatch.Reset();
            if (_isStatusQueryLate)
            {
                _isStatusQueryLate = false;
                LogHelper.Info(Name, "设备状态查询恢复");
            }

            return;
        }

        #endregion

        #region 超时：作废这一条，下一拍重发

        int timeout = QueryDataTimeOut;
        if (_queryWatch.ElapsedMilliseconds < timeout)
        {
            return;
        }

        // 驱动保留旧查询的在途项，旧回复到达前不会受理同名查询；作废句柄，下一拍换一条发。
        _queryCommand = null;
        _queryWatch.Reset();
        if (!_isStatusQueryLate)
        {
            _isStatusQueryLate = true;
            LogHelper.Warn(Name, $"设备状态查询超时（{timeout}ms）：这一条作废、接着查");
        }

        #endregion
    }

    /// <summary>
    /// 轮流查：订阅（没成前）→ 报错 → 伺服 → 速度 → 轴位（按轴节点逐轴轮，占两拍）。查询被拒就跳过这条，下拍再试。
    /// </summary>
    private RobotCommand? CreateQueryCommand(RobotDriverComponent robot)
    {
        int count = _queryCount++;
        if (!_waferEventSubscribed && count % SubscribeRetryInterval == 0)
        {
            _queryKind = QueryKind.SubscribeWaferEvent;
            return robot.SubscribeWaferEvent();
        }

        switch (count % 5)
        {
            case 0:
                _queryKind = QueryKind.DeviceError;
                return robot.QueryDeviceError();

            case 1:
                _queryKind = QueryKind.ServoOn;
                return robot.QueryServoOn();

            case 2:
                _queryKind = QueryKind.Speed;
                return robot.QuerySpeed();

            case 3:
            case 4:
                _queryKind = QueryKind.AxisPos;
                var axes = Axes;
                if (axes.Count == 0)
                {
                    return null;
                }

                // 每拍轮一根轴：X、Z、Theta、Arm1、Arm2……按 sc.xml 轴节点顺序滚。
                _queryAxis = axes[_axisScan++ % axes.Count].Name;
                return robot.QueryAxisPos(_queryAxis);
        }

        return null;
    }

    /// <summary>
    /// 只读查询的 Response 刷新模块状态。
    /// </summary>
    private void ApplyQueryResponse(RobotCommand command)
    {
        var response = command.Response!;
        switch (_queryKind)
        {
            case QueryKind.SubscribeWaferEvent:
                _waferEventSubscribed = response.IsSuccess;
                if (!response.IsSuccess)
                {
                    LogHelper.Warn(Name, $"订阅手指在位推送失败：{response.Error}");
                }

                break;

            case QueryKind.ServoOn:
                if (response.IsSuccess)
                {
                    IsServoOn = response.ServoOn;
                }

                break;

            case QueryKind.DeviceError:
                if (response.IsSuccess)
                {
                    DeviceError = response.DeviceError;
                }

                break;

            case QueryKind.Speed:
                if (response.IsSuccess)
                {
                    Speed = response.Speed;
                }

                break;

            case QueryKind.AxisPos:
                if (response.IsSuccess && response.Position.HasValue && _queryAxis is not null)
                {
                    NoteAxisPos(_queryAxis, response.Position.Value);
                }

                break;
        }
    }

    #endregion

    #region 状态发布

    private RobotDto? _lastPublishedState;

    /// <summary>
    /// 当前状态快照，状态发布与 GetState 查询共用。
    /// 未连接或停用时查询反馈（伺服使能、设备报错、速度）不可信，置 null；手指在位保留最后一次推送值；
    /// 当前站点及其伸出方向、伸出距离 Y 取最近一次发起成功的取放片，还没取放过为 北 / 0；
    /// 站点表（含各站点槽数、允许的手指）与轴坐标（sc.xml 轴节点整表）下推，界面（站点 / 手臂 / 槽位下拉、轴位表）不写死。
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
            ArmCount = ArmCount,
            Stations = _stations.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
            StationInfos = _stations.Values
                .OrderBy(station => station.Number)
                .Select(station => new RobotStationDto
                {
                    Name = station.Name,
                    Number = station.Number,
                    Direction = station.Direction,
                    Y = station.Y,
                    SlotCount = SlotCountOf(station.Name),
                    Arms = ArmsOf(station),
                    Kind = KindOf(station.Name),
                })
                .ToList(),
            Arms = Arms
                .Select(arm => new RobotArmDto { Arm = arm.Number, HasWafer = arm.HasWafer ?? false })
                .ToList(),
            LedgerSlots = WaferLedgerSnapshot.SlotsOf(Name),
        };

        var robot = _robot;
        dto.IsConnected = robot?.IsConnected ?? false;

        // 轴位表按 sc.xml 轴节点整表下发（轴名总在，界面没数据也列得出轴）：
        // 连上了才带坐标，还没查到或没连上的轴坐标为 null——组件里存的是断线前的旧值，不作数。
        bool trusted = dto.IsConnected && IsEnable;
        foreach (var axis in Axes)
        {
            dto.AxisPositions.Add(new RobotAxisPositionDto
            {
                Name = axis.Name,
                Position = trusted ? axis.Position : null,
            });
        }

        if (dto.IsConnected && IsEnable)
        {
            dto.IsServoOn = IsServoOn;
            dto.DeviceError = DeviceError;
            dto.Speed = Speed;
        }

        return dto;
    }

    /// <summary>
    /// 站点槽数：取站点模块在 sc.xml 里配的 SlotCount（LoadPort 25、腔体 1），不在站点表里重复配。
    /// 搬运模块表还没绑好、或站点名不是可服务工位时为 0。
    /// </summary>
    private static int SlotCountOf(string station)
    {
        var transfers = TransferManager.Current;
        if (transfers is null)
        {
            return 0;
        }

        if (!transfers.TryGetStation(station, out var module))
        {
            return 0;
        }

        return module.SlotCount;
    }

    /// <summary>
    /// 站点是哪一类模块（调度图按它选卡片）：跟槽数一样从搬运模块表认；表还没绑好、或名字不是可服务工位时算 Other。
    /// </summary>
    private static StationKind KindOf(string station)
    {
        var transfers = TransferManager.Current;
        if (transfers is null)
        {
            return StationKind.Other;
        }

        if (!transfers.TryGetStation(station, out var module))
        {
            return StationKind.Other;
        }

        return module switch
        {
            BaseLoadPortModule => StationKind.LoadPort,
            BaseChamberModule => StationKind.Chamber,
            _ => StationKind.Other,
        };
    }

    /// <summary>
    /// 站点允许的手指：sc.xml 站点节点配了 Arms 就按它；没配就是所有手指（sc.xml 里配的 Arm 节点）。
    /// </summary>
    private List<int> ArmsOf(RobotStation station)
    {
        if (station.Arms.Count > 0)
        {
            return [.. station.Arms];
        }

        return [.. Arms.Select(arm => arm.Number)];
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

    #region IRobot 契约

    /// <summary>
    /// 发起 Home。机型实现：Begin(RobotAction.Home, new ...Operation(...))。
    /// </summary>
    public abstract ModuleOperation? Home();

    /// <summary>
    /// 模块初始化（动硬件，重写 BaseModule 的 InitModule）：回原点——Home 就是机械手的初始化（NotInit → Idle）。
    /// 人或调度才调，开机不调。返回 Home 操作，调用方等它做完；状态不允许时为 null。
    /// </summary>
    public override ModuleOperation? InitModule()
    {
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
    /// 发起 Pick：station 为站点表中的模块名（如 LoadPort1），站点号取本机械手站点表；
    /// 未配置的站点、站点不许用的手指（站点节点的 Arms）被拒（返回 null）。
    /// </summary>
    public ModuleOperation? Pick(int arm, string station, int slot)
    {
        return BeginAtStation(RobotAction.Pick, arm, station, slot,
            config => Begin(RobotAction.Pick, CreatePickOperation(arm, config.Number, slot)));
    }

    /// <summary>
    /// 发起 Place：station 为站点表中的模块名，站点号取本机械手站点表；
    /// 未配置的站点、站点不许用的手指被拒（返回 null）。
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

        // 这个站点不许用这只手：手动、自动下的单一样拒。
        if (!config.AllowsArm(arm))
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
    /// 装机停用、或驱动组件没挂起来（装配里组件初始化没做成）都不发动作。
    /// </summary>
    protected override bool CanBeginAction => IsEnable && _robot is not null;

    /// <summary>
    /// 操作终结（状态已由基类落好）：记晶圆账，失败的动作报警。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        UpdateLedger(operation);
        RaiseActionFailedAlarm(operation);
    }

    #endregion

    /// <summary>
    /// 扫描周期
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        LoopQueryStatus(); //设备状态轮询
        CheckDeviceAlarm(); //检查报警
        PublishState(); //推送状态
    }

    #region 报警

    /// <summary>
    /// 设备报警跟着设备报错走：有报错就报。报错没了也不清——报警只能人工 Reset 清。
    /// 没连上时 DeviceError 是上一次的旧值，不作数，不判。
    /// </summary>
    private void CheckDeviceAlarm()
    {
        var robot = _robot;
        if (robot is not null && robot.IsConnected && !string.IsNullOrEmpty(DeviceError))
        {
            RaiseAlarm(RobotDeviceAlarm);
        }
    }

    /// <summary>
    /// 动作类报警：失败就报；动作成功也不清，只能人工 Reset 清。
    /// 人为急停顶掉的动作不报——那是操作员自己按的，不是故障。机型有自己的报警分法就重写（自己要管的情况先判、报了就 return，其余交给 base；整套换掉不调 base）；
    /// 在操作终结的回调里调、在模块锁里：只报警，别等待、别去拿别的模块的锁。
    /// </summary>
    protected virtual void RaiseActionFailedAlarm(ModuleOperation operation)
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

        var ledger = WaferManagerComponent.Current;
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
