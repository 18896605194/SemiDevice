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

    public RobotDriverComponent? _robot { get; private set; }

    public IReadOnlyList<RobotAxisComponent> Axes { get; private set; } = Array.Empty<RobotAxisComponent>();
    public IReadOnlyList<RobotArmComponent> Arms { get; private set; } = Array.Empty<RobotArmComponent>();

    /// <summary>
    /// 晶圆账：组件初始化时取一次；没配晶圆账、或机械手停用（不登记账）为 null。
    /// </summary>
    private WaferManagerComponent? _waferManager;

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

        // 停用的机械手也要收轴表，所以放在判 IsEnable 前面：界面照样列出轴名。
        // 手指也是轴：Axes 是整张轴表（含手指），Arms 从里面挑出手指、按手指号排。
        Axes = FindChildren<RobotAxisComponent>();
        Arms = Axes.OfType<RobotArmComponent>().OrderBy(arm => arm.Number).ToList();

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

        // 站点表里配的手指必须存在，界面不会给出一只用不了的手。
        foreach (var station in _stations.Values)
        {
            foreach (int arm in station.Arms)
            {
                if (Arms.FirstOrDefault(item => item.Number == arm) is null)
                {
                    LogHelper.Error(Name, $"sc.xml 站点 {station.Name} 的 Arms 配了 Arm{arm}，但没配这个手指节点");
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

        #endregion

        // 晶圆账注册：手指在账里也是槽位，槽号 = 手指号
        _waferManager = WaferManagerComponent.Current;
        _waferManager?.RegisterLocation(Name, Arms.Count);

        bool childrenInitialized = base.InitComponent();
        return robot is not null && childrenInitialized;
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
                RobotArmComponent? arm = Arms.FirstOrDefault(item => item.Number == evt.Arm);
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
    private bool _isStatusQueryLate;
    private bool _waferEventSubscribed;

    /// <summary>
    /// 设备状态轮询（扫描线程，跟 LoadPort 一个路子）：在途那条没回来、没超时就等，超时就作废它；
    /// 回来了或作废了，就往下走一步——写上一条的结果、发这一圈的下一条；一圈查完，下一拍从头再查。
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

        _queryRound ??= QueryRound(robot).GetEnumerator();
        if (_queryRound.MoveNext())
        {
            _query = _queryRound.Current;
            _queryWatch.Restart();
            return;
        }

        // 一圈查完：下一拍从头再查
        _queryRound = null;
        _query = null;

        #endregion
    }

    /// <summary>
    /// 一圈查什么、什么顺序、回来写到哪，从上往下写：yield return 一条 = 发出去，等它回来（或超时作废）了再往下走。
    /// 发不出去、没查成的那一项这圈不写，接着查下一项。加一种查询：照着加一段。
    /// </summary>
    private IEnumerable<RobotCommand?> QueryRound(RobotDriverComponent robot)
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

    public abstract ModuleOperation? PowerOn();

    public abstract ModuleOperation? PowerOff();

    public abstract ModuleOperation? Home();

    public override ModuleOperation? InitModule()
    {
        return Home();
    }

    public override ModuleOperation? Reset()
    {
        base.Reset();
        return ResetDevice();
    }

    protected abstract ModuleOperation? ResetDevice();

    public override ModuleOperation? Abort()
    {
        base.Abort();
        return AbortDevice();
    }

    protected abstract ModuleOperation? AbortDevice();

    public ModuleOperation? Pick(int arm, string station, int slot)
    {
        return BeginAtStation(RobotAction.Pick, arm, station, slot,
            config => Begin(RobotAction.Pick, CreatePickOperation(arm, config.Number, slot)));
    }

    protected abstract ModuleOperation CreatePickOperation(int arm, int stationNumber, int slot);


    public ModuleOperation? Place(int arm, string station, int slot)
    {
        return BeginAtStation(RobotAction.Place, arm, station, slot,
            config => Begin(RobotAction.Place, CreatePlaceOperation(arm, config.Number, slot)));
    }

    protected abstract ModuleOperation CreatePlaceOperation(int arm, int stationNumber, int slot);

    /// <summary>
    /// 换片：默认不支持，返回 null（被拒）。机械手支持换片的，在机型里重写。
    /// </summary>
    public virtual ModuleOperation? Swap(int pickArm, int placeArm, string station, int slot)
    {
        return null;
    }

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
            _transferIntent = new TransferIntent(action, arm, station, slot);
            return operation;
        }
    }

    
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

    private sealed record TransferIntent(RobotAction Action, int Arm, string Station, int Slot);

    private TransferIntent? _transferIntent;

    private void UpdateLedger(ModuleOperation operation)
    {
        var intent = _transferIntent;
        _transferIntent = null;
        if (intent is null || !operation.IsSuccess || _waferManager is null)
        {
            return;
        }

        bool moved = intent.Action == RobotAction.Pick
            ? _waferManager.Move(intent.Station, intent.Slot, Name, intent.Arm)
            : _waferManager.Move(Name, intent.Arm, intent.Station, intent.Slot);

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
