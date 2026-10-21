using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
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
    /// <summary>
    /// 模块类型：机械手
    /// </summary>
    public override StationKind Kind => StationKind.Robot;

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

    [SCEditor("True", "_robot",
        "手指在位主动推送：True = 连上先订阅，手指上有没有片由控制器主动推；False = 这台机械手不带这个功能，不发订阅（手指在位一直是没收到）")]
    public bool WaferEventEnabled { get; set; } = true;

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
        @default: "1000", description: "设备状态查询周期（一圈：报错、伺服、速度、各轴坐标）")]
    public int QueryStatusIntervalMs
    {
        get { return GetEcInt(nameof(QueryStatusIntervalMs)); }
        set { SetEcInt(nameof(QueryStatusIntervalMs), value); }
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

    public IRobotDriverComponent? _robot { get; private set; }

    public IReadOnlyList<IRobotAxis> Axes { get; private set; } = Array.Empty<IRobotAxis>();
    public IReadOnlyList<IRobotArm> Arms { get; private set; } = Array.Empty<IRobotArm>();

    /// <summary>
    /// 晶圆账：组件初始化时取一次；没配晶圆账、或机械手停用（不登记账）为 null。
    /// </summary>
    private ILedgerManager? _ledger;

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

    public override bool InitComponent()
    {
        #region 轴与手指

        Axes = FindChildren<IRobotAxis>();
        Arms = Axes.OfType<IRobotArm>().OrderBy(arm => arm.Number).ToList();
        foreach (var arm in Arms)
        {
            arm.SetRobot(Name);
        }

        // 账本槽位 = 手指号，所以手指号要唯一、从 1 连续到 N。
        for (int index = 0; index < Arms.Count; index++)
        {
            var arm = Arms[index];
            if (arm.Number != index + 1)
            {
                throw new InvalidOperationException(
                    $"sc.xml 节点 {arm.FullPath} 的手指号 {arm.Number} 不合法：要唯一、从 1 连续到 {Arms.Count}（晶圆账槽位 = 手指号）。");
            }
        }

        if (Axes.Count == 0)
        {
            LogHelper.Error(Name, "sc.xml 没配轴节点：本模块下要有 X / Z / Theta / Arm* 这些 RobotAxisComponent / RobotArmComponent 节点");
        }

        #endregion

        #region 站点表校验

        // 站点表里配的手指必须存在：配了这台机械手没有的手，开机就报出来，别等取放时才拒
        foreach (var station in _stations.Values)
        {
            foreach (int arm in station.PickArms)
            {
                if (Arms.All(item => item.Number != arm))
                {
                    throw new InvalidOperationException(
                        $"sc.xml 节点 {FullPath}.Stations.{station.Name} 的 PickArms 配了 Arm{arm}，但这台机械手没配这个手指节点。");
                }
            }

            foreach (int arm in station.PlaceArms)
            {
                if (Arms.All(item => item.Number != arm))
                {
                    throw new InvalidOperationException(
                        $"sc.xml 节点 {FullPath}.Stations.{station.Name} 的 PlaceArms 配了 Arm{arm}，但这台机械手没配这个手指节点。");
                }
            }
        }

        #endregion

        // 停用的机械手：不连驱动、不登记晶圆账
        if (!IsEnable)
        {
            return true;
        }

        #region 机械手驱动

        var robot = FindChild<IRobotDriverComponent>();
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

        #endregion

        // 晶圆账注册：手指在账里也是槽位，槽号 = 手指号
        _ledger = IocHelper.GetService<ILedgerManager>();
        _ledger?.RegisterLocation(Name, Arms.Count);

        bool childrenInitialized = base.InitComponent();
        return robot is not null && childrenInitialized;
    }

    /// <summary>
    /// 搬运管理（开机 Bind 时取）：站点的槽数、类型从它的模块表认；没装是 null
    /// </summary>
    private ITransferManager? _transfers;

    public override void Bind()
    {
        _transfers = IocHelper.GetService<ITransferManager>();
    }

    public void Close()
    {
        _robot?.Close();
    }


    private void OnDeviceEvent(RobotDeviceEvent evt)
    {
        switch (evt.Kind)
        {
            case RobotDeviceEventKind.WaferPresence:
                IRobotArm? arm = Arms.FirstOrDefault(item => item.Number == evt.Arm);
                if (arm is not null)
                {
                    arm.UpdateWaferPresence(evt.HasWafer);
                }
                else
                {
                    // 设备推了 sc.xml 里没配的手指：不凭空长一只手，记日志忽略。
                    LogHelper.Warn(Name, $"收到手指 {evt.Arm} 的在位推送，但没配这个手指节点");
                }

                break;

            case RobotDeviceEventKind.DeviceError:
                DeviceError = evt.Content;
                break;
        }
    }

    #endregion

    #region 设备状态查询

    /// <summary>这一圈查到哪了；一圈查完为 null，下一拍从头再查一圈。</summary>
    private IEnumerator<RobotCommand?>? _queryRound;

    /// <summary>在途的那条查询：跟 LoadPort 一样同一时间只有一条，回来了（或超时作废了）才发下一条。</summary>
    private RobotCommand? _query;

    private readonly Stopwatch _queryWatch = new();

    /// <summary>这一圈从什么时候开始查的：新的一圈要离上一圈开始满一个周期（EC QueryStatusIntervalMs）。</summary>
    private readonly Stopwatch _roundWatch = new();

    private bool _isStatusQueryLate;
    private bool _waferEventSubscribed;

    /// <summary>
    /// 设备状态轮询（扫描线程，跟 LoadPort 一个路子）：在途那条没回来、没超时就等，超时就作废它；
    /// 回来了或作废了，就往下走一步——写上一条的结果、发这一圈的下一条；一圈查完，离这一圈开始满一个周期（EC QueryStatusIntervalMs）再从头查。
    /// 查什么、什么顺序、回来写到哪，都在 <see cref="QueryRound"/> 里。
    /// </summary>
    private void LoopQueryStatus()
    {
        var robot = _robot;
        if (!IsEnable || robot is null)
        {
            return;
        }

        #region 没连上：这一圈作废

        // 连上以后从头查；在位推送的订阅跟着连接走，也要重新订。
        if (!robot.IsConnected)
        {
            _queryRound = null;
            _query = null;
            _waferEventSubscribed = false;
            return;
        }

        #endregion

        #region 等在途那条：没回来、没超时就等；超时作废

        var query = _query;
        if (query is not null && !query.IsCompleted)
        {
            int timeout = QueryDataTimeOut;
            if (_queryWatch.ElapsedMilliseconds < timeout)
            {
                return;
            }

            // 回复丢了的话，这条会一直占着驱动的在途位，同名查询再也发不出去，所以要作废。
            robot.Abandon(query, "Timeout");
            if (!_isStatusQueryLate)
            {
                _isStatusQueryLate = true;
                LogHelper.Warn(Name, $"设备状态查询超时（{timeout}ms）：这一条作废、接着查");
            }
        }
        else if (_isStatusQueryLate && ReplyOf(query) is not null)
        {
            _isStatusQueryLate = false;
            LogHelper.Info(Name, "设备状态查询恢复");
        }

        #endregion

        #region 往下走一步：写上一条的结果、发下一条

        // 新的一圈离上一圈开始不满一个周期就先不查；一圈里的各条照旧一条回来接着发下一条
        if (_queryRound is null)
        {
            if (_roundWatch.IsRunning && _roundWatch.ElapsedMilliseconds < QueryStatusIntervalMs)
            {
                return;
            }

            _queryRound = QueryRound(robot).GetEnumerator();
            _roundWatch.Restart();
        }

        if (_queryRound.MoveNext())
        {
            _query = _queryRound.Current;
            _queryWatch.Restart();
            return;
        }

        // 一圈查完：满一个周期再从头查
        _queryRound = null;
        _query = null;

        #endregion
    }

    /// <summary>
    /// 一圈查什么、什么顺序、回来写到哪，从上往下写：yield return 一条 = 发出去，等它回来（或超时作废）了再往下走。
    /// 发不出去、没查成的那一项这圈不写，接着查下一项。加一种查询：照着加一段。
    /// </summary>
    private IEnumerable<RobotCommand?> QueryRound(IRobotDriverComponent robot)
    {
        // 手指在位推送：SC 开着又还没订上，每圈开头订一次，订上为止
        if (WaferEventEnabled && !_waferEventSubscribed)
        {
            var subscribe = robot.SubscribeWaferEvent();
            yield return subscribe;
            _waferEventSubscribed = ReplyOf(subscribe) is not null;
        }

        var error = robot.QueryDeviceError();
        yield return error;
        var errorReply = ReplyOf(error);
        if (errorReply is not null)
        {
            DeviceError = errorReply.DeviceError;
        }

        var servo = robot.QueryServoOn();
        yield return servo;
        var servoReply = ReplyOf(servo);
        if (servoReply is not null)
        {
            IsServoOn = servoReply.ServoOn;
        }

        var speed = robot.QuerySpeed();
        yield return speed;
        var speedReply = ReplyOf(speed);
        if (speedReply is not null)
        {
            Speed = speedReply.Speed;
        }

        // 每根轴（含手指）查一次坐标，按 sc.xml 轴节点的先后
        foreach (var axis in Axes)
        {
            var position = robot.QueryAxisPos(axis.Name);
            yield return position;
            var positionReply = ReplyOf(position);
            if (positionReply is not null)
            {
                axis.UpdatePosition(positionReply.Position);
            }
        }
    }

    /// <summary>
    /// 查成了的回复；没发出去、还没回来、失败、超时作废的都是 null。
    /// </summary>
    private static RobotResponse? ReplyOf(RobotCommand? command)
    {
        if (command is null || !command.IsCompleted)
        {
            return null;
        }

        var response = command.Response;
        if (response is null || !response.IsSuccess)
        {
            return null;
        }

        return response;
    }

    #endregion

    #region 状态发布

    private RobotDto? _lastPublishedState;

    /// <summary>
    /// 当前状态快照，状态发布与 GetState 查询共用。
    /// 未连接或停用时查询反馈（伺服使能、设备报错、速度）不可信，置 null；手指在位保留最后一次推送值；
    /// 当前站点及其伸出方向、伸出距离 Y 取最近一次发起成功的取放片，还没取放过为 北 / 0；
    /// 手指伸出 = 这一趟取放还没做完（发出指令时记下、做完清掉）的那只手；
    /// 站点表（含各站点槽数、允许的手指）与轴坐标（sc.xml 轴节点整表）下推，界面（站点 / 手臂 / 槽位下拉、轴位表）不写死。
    /// </summary>
    public RobotDto CreateStateDto()
    {
        var station = _currentStation;
        var intent = _transferIntent;
        var dto = new RobotDto
        {
            Name = Name,
            State = State,
            Mode = Mode,
            Station = station?.Name,
            Direction = station?.Direction ?? RobotDirection.North,
            Y = station?.Y ?? 0,
            ArmCount = Arms.Count,
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
                    PickArms = ArmsOf(station.PickArms),
                    PlaceArms = ArmsOf(station.PlaceArms),
                    Kind = KindOf(station.Name),
                })
                .ToList(),
            Arms = Arms
                .Select(arm => new RobotArmDto
                {
                    Arm = arm.Number,
                    HasWafer = arm.HasWafer ?? false,
                    IsExtended = intent is not null && intent.Arm == arm.Number,
                })
                .ToList(),
            LedgerSlots = WaferLedgerSnapshot.SlotsOf(_ledger, Name),
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
    private int SlotCountOf(string station)
    {
        var transfers = _transfers;
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
    /// 站点是哪一类（调度图按它选卡片）：照抄站点自己报的，机械手不判断具体站型；表还没绑好、或名字不是可服务工位时算其他
    /// </summary>
    private StationKind KindOf(string station)
    {
        var transfers = _transfers;
        if (transfers is null)
        {
            return StationKind.Other;
        }

        if (!transfers.TryGetStation(station, out var module))
        {
            return StationKind.Other;
        }

        return module.Kind;
    }

    /// <summary>
    /// 推给界面的站点手指（PickArms 或 PlaceArms）：配了就按它，没配就是所有手指
    /// </summary>
    private List<int> ArmsOf(IReadOnlyList<int> configured)
    {
        if (configured.Count > 0)
        {
            return [.. configured];
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

    // 跟 LoadPort 同一套：每个动作的入口在模块锁里按 状态 → 资源 → 互锁 查（三项怎么分见 BaseModule"动作检查"区），
    // 哪项不过就带原因返回、机械手不动，都过了才挂上设备动作。设备动作平台给默认（RobotCommandOperation），机型动作不一样才重写 CreateXxxOperation。
    // 取放之前站点要先准备好（腔体开门、LoadPort 要 Load 着），那一段在搬运管理里：手动页、自动跑货的取放都经搬运管理走到这里。

    /// <summary>
    /// 检查都过了，挂上设备动作；挂不上（检查和挂在同一把锁里，正常不会）回笼统的被拒 + 当前状态。
    /// </summary>
    private HandleResult<ModuleOperation> Started(RobotAction action, ModuleOperation operation)
    {
        var started = Begin(action, operation);
        if (started is null)
        {
            return HandleResult<ModuleOperation>.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
        }

        return HandleResult<ModuleOperation>.Success(started);
    }

    #region Home

    /// <summary>
    /// Home：全轴回原点。调用方：手动页、模块初始化。
    /// </summary>
    public HandleResult<ModuleOperation> Home()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(RobotAction.Home) ?? HomeInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(RobotAction.Home, CreateHomeOperation());
        }
    }

    protected virtual ModuleOperation CreateHomeOperation()
    {
        return new RobotCommandOperation("Home", () => _robot?.Home(), () => HomeTimeout);
    }

    #endregion

    #region Pick / Place

    /// <summary>
    /// Pick：用这只手从站点的这个槽取片。调用方：搬运管理（手动页的取片、自动跑货都经它，站点先准备好）。
    /// </summary>
    public HandleResult<ModuleOperation> Pick(int arm, string station, int slot)
    {
        lock (OperationGate)
        {
            var rejected = CheckPick(arm, station, slot);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return StartTransfer(RobotAction.Pick, arm, station, slot);
        }
    }

    /// <summary>
    /// Pick 能不能做（只查不动）：状态 → 资源 → 互锁。搬运管理在给站点落"取放中"标记之前先问它——
    /// 机械手这时候不能动（没连上、正忙、账不对）就当没动手处理，把准备好的站点还回去，不留着等人工。
    /// </summary>
    public HandleResult? CheckPick(int arm, string station, int slot)
    {
        lock (OperationGate)
        {
            return CheckState(RobotAction.Pick) ?? CheckPickResource(arm, station, slot) ?? PickInterlock(arm, station, slot);
        }
    }

    /// <summary>
    /// Place：把这只手上的片放到站点的这个槽。调用方同 <see cref="Pick"/>。
    /// </summary>
    public HandleResult<ModuleOperation> Place(int arm, string station, int slot)
    {
        lock (OperationGate)
        {
            var rejected = CheckPlace(arm, station, slot);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return StartTransfer(RobotAction.Place, arm, station, slot);
        }
    }

    /// <summary>
    /// Place 能不能做（只查不动）：状态 → 资源 → 互锁。用法同 <see cref="CheckPick"/>。
    /// </summary>
    public HandleResult? CheckPlace(int arm, string station, int slot)
    {
        lock (OperationGate)
        {
            return CheckState(RobotAction.Place) ?? CheckPlaceResource(arm, station, slot) ?? PlaceInterlock(arm, station, slot);
        }
    }

    /// <summary>
    /// Pick 的资源检查：站点、手指能用，晶圆账上这个槽有片、这只手上没片。
    /// 账不对就不动——去空槽取片、拿着片再去取都会碰片；要先在账单调整页按实物改账。
    /// </summary>
    private HandleResult? CheckPickResource(int arm, string station, int slot)
    {
        var rejected = CheckPickArm(arm, station);
        if (rejected is not null)
        {
            return rejected;
        }

        var ledger = _ledger;
        if (ledger is null || !ledger.IsEnable)
        {
            return null;
        }

        if (ledger.Get(station, slot) is null)
        {
            return HandleResult.Fail(ErrorCodes.WaferNoWafer, station, slot.ToString(CultureInfo.InvariantCulture));
        }

        var held = ledger.Get(Name, arm);
        if (held is not null)
        {
            return HandleResult.Fail(ErrorCodes.RobotArmHasWafer, Name, arm.ToString(CultureInfo.InvariantCulture), held.WaferId);
        }

        return null;
    }

    /// <summary>
    /// Place 的资源检查：站点、手指能用，晶圆账上这只手有片、这个槽上没片（往有片的槽上放会叠片）。
    /// </summary>
    private HandleResult? CheckPlaceResource(int arm, string station, int slot)
    {
        var rejected = CheckPlaceArm(arm, station);
        if (rejected is not null)
        {
            return rejected;
        }

        var ledger = _ledger;
        if (ledger is null || !ledger.IsEnable)
        {
            return null;
        }

        var armComponent = FindArm(arm);
        if (armComponent is not null && !armComponent.LedgerHasWafer)
        {
            return HandleResult.Fail(ErrorCodes.RobotArmNoWafer, Name, arm.ToString(CultureInfo.InvariantCulture));
        }

        var occupant = ledger.Get(station, slot);
        if (occupant is not null)
        {
            return HandleResult.Fail(ErrorCodes.WaferSlotOccupied, station, slot.ToString(CultureInfo.InvariantCulture), occupant.WaferId);
        }

        return null;
    }

    /// <summary>
    /// 取片的手：站点在本机械手的站点表里、这只手存在、这个站许它取片（sc.xml 站点节点的 PickArms）
    /// </summary>
    private HandleResult? CheckPickArm(int arm, string station)
    {
        if (!TryGetStation(station, out var config))
        {
            return HandleResult.Fail(ErrorCodes.StationNotFound, Name, station);
        }

        if (!config.AllowsPick(arm) || Arms.All(item => item.Number != arm))
        {
            return HandleResult.Fail(ErrorCodes.ArmNotAllowed, Name, station, arm.ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }

    /// <summary>
    /// 放片的手：站点在本机械手的站点表里、这只手存在、这个站许它放片（sc.xml 站点节点的 PlaceArms）
    /// </summary>
    private HandleResult? CheckPlaceArm(int arm, string station)
    {
        if (!TryGetStation(station, out var config))
        {
            return HandleResult.Fail(ErrorCodes.StationNotFound, Name, station);
        }

        if (!config.AllowsPlace(arm) || Arms.All(item => item.Number != arm))
        {
            return HandleResult.Fail(ErrorCodes.ArmNotAllowed, Name, station, arm.ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }

    /// <summary>
    /// 挂上取 / 放片，同时记下去了哪个站点（推给界面画机械手朝向）和这一趟的意图（做完按它记账；没做完时界面画这只手伸出）。
    /// 挂操作和记意图在同一把锁里：否则扫描线程可能在意图记上之前就把操作终结了，这一趟就不记账。
    /// </summary>
    private HandleResult<ModuleOperation> StartTransfer(RobotAction action, int arm, string station, int slot)
    {
        if (!TryGetStation(station, out var config))
        {
            return Rejected(HandleResult.Fail(ErrorCodes.StationNotFound, Name, station));
        }

        bool pick = action == RobotAction.Pick;
        var noWafer = FindExpectedWafer(pick, arm, station, slot, out var expectedWaferId);
        if (noWafer is not null)
        {
            return Rejected(noWafer);
        }

        ModuleOperation motion;
        if (pick)
        {
            motion = CreatePickOperation(arm, config.Number, slot);
        }
        else
        {
            motion = CreatePlaceOperation(arm, config.Number, slot);
        }

        return StartWithLedger(action, motion, new TransferIntent(action, arm, station, slot, expectedWaferId), config);
    }

    /// <summary>
    /// 开着晶圆账时认定要搬的那一片（取片看站点这一槽、放片看这只手）：复用已有晶圆 Id，动作期间来源槽补成同样的业务片号也不会误移另一片的账；没片就拒
    /// </summary>
    private HandleResult? FindExpectedWafer(bool pick, int arm, string station, int slot, out Guid? expectedWaferId)
    {
        expectedWaferId = null;
        var ledger = _ledger;
        if (ledger is null || !ledger.IsEnable)
        {
            return null;
        }

        string fromModule = Name;
        int fromSlot = arm;
        if (pick)
        {
            fromModule = station;
            fromSlot = slot;
        }

        var wafer = ledger.Get(fromModule, fromSlot);
        if (wafer is null)
        {
            if (pick)
            {
                return HandleResult.Fail(ErrorCodes.WaferNoWafer, station, slot.ToString(CultureInfo.InvariantCulture));
            }

            return HandleResult.Fail(ErrorCodes.RobotArmNoWafer, Name, arm.ToString(CultureInfo.InvariantCulture));
        }

        expectedWaferId = wafer.Id;
        return null;
    }

    /// <summary>
    /// 设备动作套上持片检查和记账再挂上：机型只补设备动作，持片校验和记账由公共取放操作负责，记账通过后才向搬运管理确认成功
    /// </summary>
    private HandleResult<ModuleOperation> StartWithLedger(RobotAction action, ModuleOperation motion, TransferIntent intent, RobotStation config)
    {
        var armComponent = FindArm(intent.Arm);
        IRobotArm? sensorArm = null;
        if (armComponent is not null && armComponent.IsWaferCheckEnabled)
        {
            sensorArm = armComponent;
        }

        var operation = new RobotTransferOperation(motion, Name, sensorArm, expectWafer: intent.Action == RobotAction.Pick,
            commitLedger: () => CommitTransferLedger(intent));
        var started = Started(action, operation);
        if (started.IsSuccess)
        {
            _currentStation = config;
            _transferIntent = intent;
        }

        return started;
    }

    protected virtual ModuleOperation CreatePickOperation(int arm, int stationNumber, int slot)
    {
        return new RobotCommandOperation("Pick", () => _robot?.Pick(arm, stationNumber, slot), () => PickTimeout);
    }

    protected virtual ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot)
    {
        return new RobotCommandOperation("Place", () => _robot?.Place(arm, stationNumber, slot), () => PlaceTimeout);
    }

    /// <summary>
    /// 换片：默认不支持，返回 null（被拒）。机械手支持换片的，在机型里重写。
    /// </summary>
    public virtual ModuleOperation? Swap(int pickArm, int placeArm, string station, int slot)
    {
        return null;
    }

    #endregion

    #region 分段取放（伸臂 → 停在站里等站点交接 → 缩臂）

    // 能分段取放的机械手类在类上声明 ITransferRobotStep（下面四个方法就是它的实现），再重写四个 CreateExtend / RetractXxxOperation 给出设备动作。
    // 伸臂做完机械手停在"伸着"（PickExtended / PlaceExtended），手还在站里，状态表只收缩臂和急停；片在伸臂时认定，缩臂做完按它查持片、记账。

    /// <summary>
    /// 分段取片第一步：检查跟 Pick 一样；手伸到片下方停住，做完停在"取片伸着"
    /// </summary>
    public HandleResult<ModuleOperation> ExtendPick(int arm, string station, int slot)
    {
        lock (OperationGate)
        {
            var rejected = CheckState(RobotAction.ExtendPick) ?? CheckPickResource(arm, station, slot) ?? PickInterlock(arm, station, slot);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return StartExtend(RobotAction.ExtendPick, arm, station, slot);
        }
    }

    /// <summary>
    /// 分段取片第二步：只在"取片伸着"、而且是伸进去的那只手那一槽时收；缩回后查持片、片记到手上
    /// </summary>
    public HandleResult<ModuleOperation> RetractPick(int arm, string station, int slot)
    {
        lock (OperationGate)
        {
            var rejected = CheckState(RobotAction.RetractPick) ?? CheckExtended(RobotAction.Pick, arm, station, slot);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return StartRetract(RobotAction.RetractPick, _transferIntent!);
        }
    }

    /// <summary>
    /// 分段放片第一步：检查跟 Place 一样；带着片伸到放片位停住，做完停在"放片伸着"
    /// </summary>
    public HandleResult<ModuleOperation> ExtendPlace(int arm, string station, int slot)
    {
        lock (OperationGate)
        {
            var rejected = CheckState(RobotAction.ExtendPlace) ?? CheckPlaceResource(arm, station, slot) ?? PlaceInterlock(arm, station, slot);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return StartExtend(RobotAction.ExtendPlace, arm, station, slot);
        }
    }

    /// <summary>
    /// 分段放片第二步：只在"放片伸着"、而且是伸进去的那只手那一槽时收；缩回后查持片、片记到站点
    /// </summary>
    public HandleResult<ModuleOperation> RetractPlace(int arm, string station, int slot)
    {
        lock (OperationGate)
        {
            var rejected = CheckState(RobotAction.RetractPlace) ?? CheckExtended(RobotAction.Place, arm, station, slot);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return StartRetract(RobotAction.RetractPlace, _transferIntent!);
        }
    }

    /// <summary>
    /// 缩臂要对得上伸进去的那一趟：同样是取片（或放片）、同一只手、同一个站、同一槽
    /// </summary>
    private HandleResult? CheckExtended(RobotAction action, int arm, string station, int slot)
    {
        var intent = _transferIntent;
        if (intent is null || intent.Action != action || intent.Arm != arm || intent.Slot != slot
            || !string.Equals(intent.Station, station, StringComparison.OrdinalIgnoreCase))
        {
            return HandleResult.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }

    /// <summary>
    /// 挂上伸臂：伸臂不动账（片还在原处），这一趟的意图记下来留给缩臂；机械手类没给伸臂动作就拒
    /// </summary>
    private HandleResult<ModuleOperation> StartExtend(RobotAction action, int arm, string station, int slot)
    {
        if (!TryGetStation(station, out var config))
        {
            return Rejected(HandleResult.Fail(ErrorCodes.StationNotFound, Name, station));
        }

        bool pick = action == RobotAction.ExtendPick;
        var noWafer = FindExpectedWafer(pick, arm, station, slot, out var expectedWaferId);
        if (noWafer is not null)
        {
            return Rejected(noWafer);
        }

        ModuleOperation? motion;
        var intentAction = RobotAction.Place;
        if (pick)
        {
            motion = CreateExtendPickOperation(arm, config.Number, slot);
            intentAction = RobotAction.Pick;
        }
        else
        {
            motion = CreateExtendPlaceOperation(arm, config.Number, slot);
        }

        if (motion is null)
        {
            return Rejected(HandleResult.Fail(ErrorCodes.RobotHandoffUnsupported, Name));
        }

        var started = Started(action, motion);
        if (started.IsSuccess)
        {
            _currentStation = config;
            _transferIntent = new TransferIntent(intentAction, arm, station, slot, expectedWaferId);
        }

        return started;
    }

    /// <summary>
    /// 挂上缩臂：按伸臂时记下的那一趟查持片、记账；机械手类没给缩臂动作就拒（手还伸着，等人工）
    /// </summary>
    private HandleResult<ModuleOperation> StartRetract(RobotAction action, TransferIntent intent)
    {
        if (!TryGetStation(intent.Station, out var config))
        {
            return Rejected(HandleResult.Fail(ErrorCodes.StationNotFound, Name, intent.Station));
        }

        ModuleOperation? motion;
        if (intent.Action == RobotAction.Pick)
        {
            motion = CreateRetractPickOperation(intent.Arm, config.Number, intent.Slot);
        }
        else
        {
            motion = CreateRetractPlaceOperation(intent.Arm, config.Number, intent.Slot);
        }

        if (motion is null)
        {
            return Rejected(HandleResult.Fail(ErrorCodes.RobotHandoffUnsupported, Name));
        }

        return StartWithLedger(action, motion, intent, config);
    }

    /// <summary>
    /// 分段取片伸臂的设备动作：能分段取放的机械手类重写，发自己的伸臂指令（伸到片下方停住，不抬片）；默认没有，分段取放被拒
    /// </summary>
    protected virtual ModuleOperation? CreateExtendPickOperation(int arm, int stationNumber, int slot)
    {
        return null;
    }

    /// <summary>
    /// 分段取片缩臂的设备动作：站点已经把片交到手上，吸住确认后缩回；默认没有
    /// </summary>
    protected virtual ModuleOperation? CreateRetractPickOperation(int arm, int stationNumber, int slot)
    {
        return null;
    }

    /// <summary>
    /// 分段放片伸臂的设备动作：带着片伸到放片位停住，有吸附的手松开吸附；默认没有
    /// </summary>
    protected virtual ModuleOperation? CreateExtendPlaceOperation(int arm, int stationNumber, int slot)
    {
        return null;
    }

    /// <summary>
    /// 分段放片缩臂的设备动作：站点已经把片接走，空手缩回；默认没有
    /// </summary>
    protected virtual ModuleOperation? CreateRetractPlaceOperation(int arm, int stationNumber, int slot)
    {
        return null;
    }

    #endregion

    #region PowerOn / PowerOff

    public HandleResult<ModuleOperation> PowerOn()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(RobotAction.PowerOn) ?? PowerOnInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(RobotAction.PowerOn, CreatePowerOnOperation());
        }
    }

    public HandleResult<ModuleOperation> PowerOff()
    {
        lock (OperationGate)
        {
            var rejected = CheckState(RobotAction.PowerOff) ?? PowerOffInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(RobotAction.PowerOff, CreatePowerOffOperation());
        }
    }

    protected virtual ModuleOperation CreatePowerOnOperation()
    {
        return new RobotCommandOperation("PowerOn", () => _robot?.PowerOn(), () => PowerTimeout);
    }

    protected virtual ModuleOperation CreatePowerOffOperation()
    {
        return new RobotCommandOperation("PowerOff", () => _robot?.PowerOff(), () => PowerTimeout);
    }

    #endregion

    #region Reset / Abort

    /// <summary>
    /// 复位：清设备报错。签名跟着组件基类（报警复位、整机复位按返回的动作等设备做完）；只查状态，被拒的原因用 <see cref="CheckReset"/> 问。
    /// </summary>
    public override ModuleOperation? Reset()
    {
        base.Reset();
        lock (OperationGate)
        {
            if (CheckReset() is not null)
            {
                return null;
            }

            return Begin(RobotAction.Reset, CreateResetOperation());
        }
    }

    public HandleResult? CheckReset()
    {
        return CheckState(RobotAction.Reset);
    }

    /// <summary>
    /// 急停：签名跟着组件基类（整机停止、搬运中止按返回的动作等设备停下）；只查状态、不看设备连没连上（见 <see cref="BaseModule.CheckState{TAction}"/>）。
    /// 被拒的原因用 <see cref="CheckAbort"/> 问。
    /// </summary>
    public override ModuleOperation? Abort()
    {
        base.Abort();
        lock (OperationGate)
        {
            if (CheckAbort() is not null)
            {
                return null;
            }

            return Begin(RobotAction.Abort, CreateAbortOperation());
        }
    }

    public HandleResult? CheckAbort()
    {
        return CheckState(RobotAction.Abort);
    }

    protected virtual ModuleOperation CreateResetOperation()
    {
        return new RobotCommandOperation("Reset", () => _robot?.ResetDrive(), () => ResetTimeout);
    }

    protected virtual ModuleOperation CreateAbortOperation()
    {
        return new RobotCommandOperation("Abort", () => _robot?.Stop(), () => AbortTimeout);
    }

    #endregion

    #region Interlock

    // 互锁：周围条件允不允许动。平台通过手指的统一持片检查契约查询反馈，机型重写加自己的条件（先调 base）。

    /// <summary>
    /// Home 前：启用持片检查的手，实际反馈跟晶圆账要对得上。账实不符先到账单调整页按实物改账再 Home，
    /// 不然回完原点接着按错账取放会碰片。反馈还没读到的手沿用现有规则，不拦 Home。
    /// </summary>
    protected virtual HandleResult? HomeInterlock()
    {
        var ledger = _ledger;
        if (ledger is null || !ledger.IsEnable)
        {
            return null;
        }

        foreach (var arm in Arms)
        {
            if (!arm.IsWaferCheckEnabled)
            {
                continue;
            }

            bool? sensed = arm.HasWafer;
            if (sensed is null)
            {
                continue;
            }

            if (sensed.Value != arm.LedgerHasWafer)
            {
                return ArmSensorMismatch(arm.Number, sensed.Value);
            }
        }

        return null;
    }

    /// <summary>取片前这只手要是空的（实际持片反馈）。</summary>
    protected virtual HandleResult? PickInterlock(int arm, string station, int slot)
    {
        return CheckArmSensor(arm, expectWafer: false);
    }

    /// <summary>放片前这只手上要有片（实际持片反馈）。</summary>
    protected virtual HandleResult? PlaceInterlock(int arm, string station, int slot)
    {
        return CheckArmSensor(arm, expectWafer: true);
    }

    /// <summary>
    /// 取放前看这只手的实际持片反馈：没启用检查就跳过；启用了但读数未知就拒绝取放。
    /// 读到的跟该有的不一样也拒绝（取片前手上有片、放片前手上没片），说明晶圆账跟实物对不上。
    /// </summary>
    private HandleResult? CheckArmSensor(int arm, bool expectWafer)
    {
        var component = FindArm(arm);
        if (component is null || !component.IsWaferCheckEnabled)
        {
            return null;
        }

        bool? sensed = component.HasWafer;
        if (sensed is null)
        {
            return HandleResult.Fail(ErrorCodes.RobotArmSensorUnknown, Name, arm.ToString(CultureInfo.InvariantCulture));
        }

        if (sensed.Value != expectWafer)
        {
            return ArmSensorMismatch(arm, sensed.Value);
        }

        return null;
    }

    /// <summary>传感器跟账对不上的原因：传感器看到有片 / 没看到片各一个错误码。</summary>
    private HandleResult ArmSensorMismatch(int arm, bool sensed)
    {
        return HandleResult.Fail(sensed ? ErrorCodes.RobotArmSensorHasWafer : ErrorCodes.RobotArmSensorNoWafer,
            Name, arm.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>按手指号找手指组件；sc.xml 没配这只手为 null。</summary>
    private IRobotArm? FindArm(int arm)
    {
        return Arms.FirstOrDefault(item => item.Number == arm);
    }

    protected virtual HandleResult? PowerOnInterlock()
    {
        return null;
    }

    protected virtual HandleResult? PowerOffInterlock()
    {
        return null;
    }

    #endregion

    #endregion

    /// <summary>
    /// 装机停用、或驱动组件没挂起来（装配里组件初始化没做成）都不发动作。
    /// </summary>
    protected override bool CanBeginAction => IsEnable && _robot is not null;

    /// <summary>
    /// 能不能动作：停用、驱动没装好之外，驱动没连上也不发——没连上照样发，指令发不出去会落 Error、报受控停止，要复位再 Home。
    /// </summary>
    protected override HandleResult? CheckAvailable()
    {
        var unavailable = base.CheckAvailable();
        if (unavailable is not null)
        {
            return unavailable;
        }

        var robot = _robot;
        if (robot is null || !robot.IsConnected)
        {
            return HandleResult.Fail(ErrorCodes.ModuleNotConnected, Name);
        }

        return null;
    }

    /// <summary>
    /// 操作终结（状态已由基类落好，成功取放已提交晶圆账）：清除交互意图（分段取放伸臂做完停在"伸着"的留着给缩臂记账），失败的动作报警。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        if (State != RobotState.PickExtended && State != RobotState.PlaceExtended)
        {
            _transferIntent = null;
        }

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

    private void CheckDeviceAlarm()
    {
        var robot = _robot;
        if (robot is not null && robot.IsConnected && !string.IsNullOrEmpty(DeviceError))
        {
            RaiseAlarm(RobotDeviceAlarm);
        }
    }

    protected virtual void RaiseActionFailedAlarm(ModuleOperation operation)
    {
        if (!operation.IsSuccess && operation.Code != ErrorCodes.Aborted)
        {
            RaiseAlarm(ControlledStopAlarm);
        }
    }

    #endregion

    #region 晶圆账

    private sealed record TransferIntent(RobotAction Action, int Arm, string Station, int Slot, Guid? ExpectedWaferId);

    private TransferIntent? _transferIntent;

    private HandleResult? CommitTransferLedger(TransferIntent intent)
    {
        var ledger = _ledger;
        if (ledger is null)
        {
            return null;
        }

        bool pick = intent.Action == RobotAction.Pick;
        string fromModule = pick ? intent.Station : Name;
        int fromSlot = pick ? intent.Slot : intent.Arm;
        string toModule = pick ? Name : intent.Station;
        int toSlot = pick ? intent.Arm : intent.Slot;
        if (intent.ExpectedWaferId is Guid expectedWaferId
            && ledger.Move(fromModule, fromSlot, toModule, toSlot, expectedWaferId))
        {
            return null;
        }

        LogHelper.Error(Name, $"{intent.Action} 动作已完成但账没记上：{fromModule}.{fromSlot:00} → {toModule}.{toSlot:00}，需确认实际片位");
        return HandleResult.Fail(ErrorCodes.RobotLedgerMoveFailed, Name,
            fromModule, fromSlot.ToString(CultureInfo.InvariantCulture),
            toModule, toSlot.ToString(CultureInfo.InvariantCulture));
    }

    #endregion
}
