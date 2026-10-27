using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Drivers.Communication;
using xyz.Drivers.Device;
using xyz.Drivers.Loadport;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

public abstract class BaseRobotModule : BaseModule, IRobot, IMode
{
    /// <summary>
    /// 模块类型：机械手
    /// </summary>
    public override StationKind Kind => StationKind.Robot;

    #region 生产 / 维护模式

    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "模块模式（生产 / 维护）")]
    public ModuleMode Mode { get; private set; } = ModuleMode.Maintenance;

    /// <summary>
    /// 切生产模式：交给调度用，只改 Mode、不经设备；机型要多做事就重写、记得调 base
    /// </summary>
    public virtual void SetProduction()
    {
        SetMode(ModuleMode.Production);
    }

    /// <summary>
    /// 切维护模式：调度不再派新活，手上的做完；重写同 <see cref="SetProduction"/>
    /// </summary>
    public virtual void SetMaintenance()
    {
        SetMode(ModuleMode.Maintenance);
    }

    private void SetMode(ModuleMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        LogHelper.Info(this, $"{mode}");
    }

    #endregion

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

    #region Machine

    [Machine("True", "_robot", "是否启用本 _robot (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    [Machine("True", "_robot",
        "手指在位主动推送：True = 连上先订阅，手指上有没有片由控制器主动推；False = 这台机械手不带这个功能，不发订阅（手指在位一直是没收到）")]
    public bool WaferEventEnabled { get; set; } = true;

    [Machine("False", "_robot",
        "扫片：True = 这台机械手用扫片，它示教点表里的 LoadPort 由它扫（扫片用示教点的 Number，机械手类要能扫片，不能就开机报错）；False = 不用，LoadPort 自己扫")]
    public bool MapEnabled { get; set; }

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

    [Alarm("_robot 设备报警", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Light,
        Description = "_robot 控制器上报报错",
        Solution = "查询报错内容，排除故障后复位并重新回原点")]
    public string RobotDeviceAlarm = nameof(RobotDeviceAlarm);

    [Alarm("_robot 受控停止", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Light,
        Description = "取放片、回原点等动作失败或超时，_robot 进入错误状态",
        Solution = "先确认手指与站点上的实际片位，再复位并重新回原点")]
    public string ControlledStopAlarm = nameof(ControlledStopAlarm);

    #endregion

    #region Component

    /// <summary>
    /// 设备通道（machine.xml 里本模块下启用的那条：串口或网口）：项目的机械手类用它发指令
    /// </summary>
    public ICommDriver? _driver { get; private set; }

    /// <summary>
    /// 这台机械手的解码器：开机时建一次交给通道
    /// </summary>
    private IFrameCodec? _codec;

    public IReadOnlyList<IRobotAxis> Axes { get; private set; } = Array.Empty<IRobotAxis>();
    public IReadOnlyList<IRobotArm> Arms { get; private set; } = Array.Empty<IRobotArm>();

    /// <summary>
    /// 晶圆账：组件初始化时取一次；没配晶圆账、或机械手停用（不登记账）为 null。
    /// </summary>
    private ILedgerManager? _ledger;

    /// <summary>
    /// 映射组件：组件初始化时取一次，账上的片转给界面用
    /// </summary>
    private IMapper? _mapper;

    #endregion

    #region 示教点表

    /// <summary>
    /// 12 寸示教点的分组名：机械手下一种晶圆尺寸一组，下面挂示教点组件
    /// </summary>
    private const string TeachPoints12Name = "Size12";

    /// <summary>
    /// 8 寸示教点的分组名
    /// </summary>
    private const string TeachPoints8Name = "Size8";

    /// <summary>
    /// 12 寸的示教点表（machine.xml 里机械手下 Size12 那组）；组件初始化时收好就不变
    /// </summary>
    private IReadOnlyDictionary<string, ITeachPoint> _teachPoints12 = new Dictionary<string, ITeachPoint>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 8 寸的示教点表（machine.xml 里机械手下 Size8 那组）；组件初始化时收好就不变
    /// </summary>
    private IReadOnlyDictionary<string, ITeachPoint> _teachPoints8 = new Dictionary<string, ITeachPoint>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 整机晶圆尺寸：组件初始化时取一次，示教点表按它选；没配按 12 寸
    /// </summary>
    private IWaferSize? _waferSize;

    /// <summary>
    /// 现在用的示教点表：整机现在几寸就是哪张
    /// </summary>
    public IReadOnlyDictionary<string, ITeachPoint> TeachPoints => TeachPointsOf(WaferSizeComponent.SizeOf(_waferSize));
    private volatile ITeachPoint? _currentTeachPoint;

    /// <summary>
    /// 某种尺寸的示教点表：8 寸是 Size8 那张，其余是 Size12 那张；没配的那种尺寸是空表（到不了）
    /// </summary>
    public IReadOnlyDictionary<string, ITeachPoint> TeachPointsOf(int size)
    {
        if (size == WaferSizeComponent.Inch8)
        {
            return _teachPoints8;
        }

        return _teachPoints12;
    }

    public bool TryGetTeachPoint(string station, [MaybeNullWhen(false)] out ITeachPoint point)
    {
        return TeachPoints.TryGetValue(station, out point);
    }

    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        // 同名的组配了两个开机就报：抄一组改另一组忘了改名，两组的示教点会被并成一张
        CheckSingleTable(setting, TeachPoints12Name);
        CheckSingleTable(setting, TeachPoints8Name);
    }

    /// <summary>
    /// 一种尺寸只能配一组
    /// </summary>
    private static void CheckSingleTable(ModuleConfig setting, string tableName)
    {
        int count = setting.Children.Count(child => string.Equals(child.Name, tableName, StringComparison.OrdinalIgnoreCase));
        if (count > 1)
        {
            throw new InvalidOperationException($"machine.xml 节点 {setting.Name}.{tableName} 配了 {count} 个，只能配一个。");
        }
    }

    /// <summary>
    /// 收示教点：下面挂的示教点组件按在 Size12 还是 Size8 组里分成两张表；挂在别处、同一组里重名、同一张表站号重复的开机就报
    /// </summary>
    private void CollectTeachPoints()
    {
        var points12 = new Dictionary<string, ITeachPoint>(StringComparer.OrdinalIgnoreCase);
        var points8 = new Dictionary<string, ITeachPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in FindChildren<ITeachPoint>())
        {
            string table = TableOf(point);
            Dictionary<string, ITeachPoint> points;
            if (string.Equals(table, TeachPoints12Name, StringComparison.OrdinalIgnoreCase))
            {
                points = points12;
            }
            else if (string.Equals(table, TeachPoints8Name, StringComparison.OrdinalIgnoreCase))
            {
                points = points8;
            }
            else
            {
                throw new InvalidOperationException(
                    $"machine.xml 节点 {point.FullPath}：示教点要挂在机械手 {Name} 下的 {TeachPoints12Name} 或 {TeachPoints8Name} 分组里（一种晶圆尺寸一组）。");
            }

            if (!points.TryAdd(point.Name, point))
            {
                throw new InvalidOperationException($"machine.xml 节点 {point.FullPath} 重复配置。");
            }
        }

        CheckDistinctNumbers(points12, TeachPoints12Name);
        CheckDistinctNumbers(points8, TeachPoints8Name);
        _teachPoints12 = points12;
        _teachPoints8 = points8;
        if (points12.Count == 0 && points8.Count == 0)
        {
            LogHelper.Error(this, $"machine.xml 没配示教点：机械手下要有 {TeachPoints12Name} / {TeachPoints8Name} 分组，下面每个站点一个 TeachPointComponent，不然哪儿都去不了");
        }
    }

    /// <summary>
    /// 一张表里站号不能重：两个站点同一个站号，机械手会把它们当成同一个工位去取放
    /// </summary>
    private void CheckDistinctNumbers(IReadOnlyDictionary<string, ITeachPoint> points, string tableName)
    {
        var seen = new Dictionary<int, string>();
        foreach (var point in points.Values)
        {
            if (seen.TryGetValue(point.Number, out string? other))
            {
                throw new InvalidOperationException(
                    $"machine.xml 机械手 {Name} 的 {tableName} 里 {other} 和 {point.Name} 的站号都是 {point.Number}：两个站点会被当成同一个工位去取放。");
            }

            seen[point.Number] = point.Name;
        }
    }

    /// <summary>
    /// 示教点挂在哪一组下：全路径里它前面那一段（Robot.Robot1.Size12.LoadPort1 是 Size12）
    /// </summary>
    private static string TableOf(ITeachPoint point)
    {
        string[] segments = point.FullPath.Split('.');
        if (segments.Length < 2)
        {
            return string.Empty;
        }

        return segments[^2];
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
                    $"machine.xml 节点 {arm.FullPath} 的手指号 {arm.Number} 不合法：要唯一、从 1 连续到 {Arms.Count}（晶圆账槽位 = 手指号）。");
            }
        }

        if (Axes.Count == 0)
        {
            LogHelper.Error(this, "machine.xml 没配轴节点：本模块下要有 X / Z / Theta / Arm* 这些 RobotAxisComponent / RobotArmComponent 节点");
        }

        #endregion

        #region 示教点表

        // 别的组件 Bind 时就会按尺寸取示教点表，所以在组件初始化时收表、取整机尺寸
        CollectTeachPoints();

        // 示教点里配的手指必须存在：配了这台机械手没有的手，开机就报出来，别等取放时才拒；两种尺寸的表都查
        CheckTeachPointArms(WaferSizeComponent.Inch12);
        CheckTeachPointArms(WaferSizeComponent.Inch8);
        _waferSize = IocHelper.GetService<IWaferSize>();

        #endregion

        // 开了扫片，机械手类得能扫（声明了 IWaferMap）；配错开机就报出来，别等 Load 时才拒
        if (MapEnabled && this is not IWaferMap)
        {
            throw new InvalidOperationException(
                $"machine.xml 节点 {FullPath} 开了 MapEnabled，但机械手类 {GetType().Name} 不能扫片（没声明 IWaferMap）。");
        }

        // 停用的机械手：不连驱动、不登记晶圆账
        if (!IsEnable)
        {
            return true;
        }

        #region 设备通道

        // 通道打开之前把解码器交给它；先摘后挂：初始化可重入，保证只挂一份；先挂再连，连上就可能有在位主动推送
        var driver = CommDriverComponent.FindIn(this);
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
            LogHelper.Error(this, "machine.xml 没有启用的设备通道：本模块下要挂串口 SerialDriverComponent 或网口 TcpDriverComponent，并启用一条（IsEnable=True）");
        }

        #endregion

        // 晶圆账注册：手指在账里也是槽位，槽号 = 手指号
        _ledger = IocHelper.GetService<ILedgerManager>();
        _ledger?.RegisterLocation(Name, Arms.Count);
        _mapper = IocHelper.GetService<IMapper>();

        bool childrenInitialized = base.InitComponent();
        return driver is not null && childrenInitialized;
    }

    /// <summary>
    /// 查一种尺寸的示教点表：PickArms / PlaceArms 配的手指这台机械手都得有
    /// </summary>
    private void CheckTeachPointArms(int size)
    {
        foreach (var point in TeachPointsOf(size).Values)
        {
            foreach (int arm in point.PickArmNumbers)
            {
                if (Arms.All(item => item.Number != arm))
                {
                    throw new InvalidOperationException(
                        $"machine.xml 机械手 {FullPath} 的 {size} 寸示教点 {point.Name} 的 PickArms 配了 Arm{arm}，但这台机械手没配这个手指节点。");
                }
            }

            foreach (int arm in point.PlaceArmNumbers)
            {
                if (Arms.All(item => item.Number != arm))
                {
                    throw new InvalidOperationException(
                        $"machine.xml 机械手 {FullPath} 的 {size} 寸示教点 {point.Name} 的 PlaceArms 配了 Arm{arm}，但这台机械手没配这个手指节点。");
                }
            }
        }
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
        _driver?.Close();
    }

    /// <summary>
    /// 设备主动上报（没有在途指令认的报文，在通道的解析线程上调）：项目的机械手类认自己品牌的报文，手指在位交给 NoteArmPresence、报错写 DeviceError
    /// </summary>
    protected virtual void OnReport(object message)
    {
    }

    /// <summary>
    /// 记一只手的在位推送；设备推了 machine.xml 里没配的手指，不凭空长一只手，记日志忽略
    /// </summary>
    protected void NoteArmPresence(int arm, bool hasWafer)
    {
        var component = FindArm(arm);
        if (component is null)
        {
            LogHelper.Warn(this, $"收到手指 {arm} 的在位推送，但没配这个手指节点");
            return;
        }

        component.UpdateWaferPresence(hasWafer);
    }

    #endregion

    #region 设备状态查询

    private StatusQueryLoop? _queries;

    /// <summary>
    /// 设备状态轮询（扫描线程）：节奏是平台的（同一时间一条在途、超时作废、一圈查完满 EC QueryStatusIntervalMs 再开下一圈），一圈查什么由 QueryDataLoop 给。
    /// 查到的值由 QueryDataLoop 自己写，这里只处理出事的：没连上、查询超时都把查到的状态清空（跟 LoadPort 一样，界面显示"不知道"，下一圈查到再填）
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
            ClearQueriedStatus();
            return;
        }

        if (happened == StatusQueryEvent.Disconnected)
        {
            ClearQueriedStatus();
            OnDisconnected();
        }
    }

    /// <summary>
    /// 查到的状态全清掉：伺服、速度、报错、各轴坐标
    /// </summary>
    private void ClearQueriedStatus()
    {
        IsServoOn = null;
        Speed = null;
        DeviceError = null;
        foreach (var axis in Axes)
        {
            axis.UpdatePosition(null);
        }
    }

    /// <summary>
    /// 一圈查什么、回来写到哪，从上往下写：yield return 一条 = 平台替它发出去、等它回来（或超时作废）再往下走；
    /// 没做成的那一项这圈不写（看指令的 IsSuccess）。默认不查（全靠设备主动推的品牌）
    /// </summary>
    protected virtual IEnumerable<DeviceCommand> QueryDataLoop()
    {
        yield break;
    }

    /// <summary>
    /// 连接刚断（断开那一拍调一次，扫描线程）：跟着连接走的东西（比如推送订阅）在这里清掉，连上以后重来
    /// </summary>
    protected virtual void OnDisconnected()
    {
    }

    #endregion

    #region 状态发布

    private RobotDto? _lastPublishedState;

    /// <summary>
    /// 当前状态快照，状态发布与 GetState 查询共用。
    /// 未连接或停用时查询反馈（伺服使能、设备报错、速度）不可信，置 null；手指在位保留最后一次推送值；
    /// 当前站点及其伸出方向、伸出距离 Y 取最近一次发起成功的取放片，还没取放过为 北 / 0；
    /// 手指伸出 = 这一趟取放还没做完（发出指令时记下、做完清掉）的那只手；
    /// 示教点表（含各站点槽数、允许的手指）与轴坐标（machine.xml 轴节点整表）下推，界面（站点 / 手臂 / 槽位下拉、轴位表）不写死。
    /// </summary>
    public RobotDto CreateStateDto()
    {
        var current = _currentTeachPoint;
        var intent = _transferIntent;
        var points = TeachPoints;
        var dto = new RobotDto
        {
            Name = Name,
            State = State,
            Mode = Mode,
            Station = current?.Name,
            Direction = current?.Direction ?? RobotDirection.North,
            Y = current?.Y ?? 0,
            ArmCount = Arms.Count,
            Stations = points.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
            TeachPoints = points.Values
                .OrderBy(point => point.Number)
                .Select(point => new TeachPointDto
                {
                    Name = point.Name,
                    Number = point.Number,
                    Direction = point.Direction,
                    Y = point.Y,
                    SlotCount = SlotCountOf(point.Name),
                    PickArms = ArmsOf(point.PickArmNumbers),
                    PlaceArms = ArmsOf(point.PlaceArmNumbers),
                    Kind = KindOf(point.Name),
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
            LedgerSlots = WaferLedgerSnapshot.SlotsOf(_ledger, _mapper, Name),
        };

        dto.IsConnected = _driver?.IsConnected ?? false;

        // 轴位表按 machine.xml 轴节点整表下发（轴名总在，界面没数据也列得出轴）：
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
    /// 站点槽数：取站点模块在 machine.xml 里配的 SlotCount（LoadPort 25、腔体 1），不在示教点里重复配。
    /// 搬运模块表还没绑好、或站点名不是可服务工位时为 0。
    /// </summary>
    private int SlotCountOf(string station)
    {
        if (_transfers is null)
        {
            return 0;
        }

        if (!_transfers.TryGetStation(station, out var module))
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
        if (_transfers is null)
        {
            return StationKind.Other;
        }

        if (!_transfers.TryGetStation(station, out var module))
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
    /// RemoteEventBus 留存最后一条消息，供界面晚订阅或重连时补发。
    /// </summary>
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

    #region IRobot 契约

    /// <summary>
    /// 模块初始化 = Home
    /// </summary>
    public override HandleResult<ModuleRoutine> InitModule()
    {
        return Home();
    }

    #region 动作入口：状态 → 资源 → 互锁

    // 跟 LoadPort 同一套：每个动作的入口在模块锁里按 状态 → 资源 → 互锁 查（三项怎么分见 BaseModule"动作检查"区），
    // 哪项不过就带原因返回、机械手不动，都过了才挂上动作。每个动作发哪几条指令由项目的机械手类在 AddXxxSteps 里加。
    // 取放之前站点要先准备好（腔体开门、LoadPort 要 Load 着），那一段在搬运管理里：手动页、自动跑货的取放都经搬运管理走到这里。

    /// <summary>
    /// 检查都过了，挂上设备动作；挂不上（检查和挂在同一把锁里，正常不会）回笼统的被拒 + 当前状态。
    /// </summary>
    private HandleResult<ModuleRoutine> Started(RobotAction action, ModuleRoutine routine)
    {
        var started = Begin(action, routine);
        if (started is null)
        {
            return HandleResult<ModuleRoutine>.Fail(ErrorCodes.ActionRejected, Name, State.ToString(CultureInfo.InvariantCulture));
        }

        return HandleResult<ModuleRoutine>.Success(started);
    }

    #region Home

    /// <summary>
    /// Home：全轴回原点。调用方：手动页、模块初始化。
    /// </summary>
    public HandleResult<ModuleRoutine> Home()
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(RobotAction.Home) ?? HomeInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            return Started(RobotAction.Home, CreateHomeRoutine());
        }
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
    /// 回零那几条：项目的机械手类用自己品牌的指令加 Command 步骤（超时用 EC HomeTimeout）
    /// </summary>
    protected abstract void AddHomeSteps(ModuleRoutine home);

    #endregion

    #region Pick / Place

    /// <summary>
    /// Pick：用这只手从站点的这个槽取片。调用方：搬运管理（手动页的取片、自动跑货都经它，站点先准备好）。
    /// </summary>
    public HandleResult<ModuleRoutine> Pick(int arm, string station, int slot)
    {
        lock (RoutineGate)
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
        lock (RoutineGate)
        {
            return CheckState(RobotAction.Pick) ?? CheckPickResource(arm, station, slot) ?? PickInterlock(arm, station, slot);
        }
    }

    /// <summary>
    /// Place：把这只手上的片放到站点的这个槽。调用方同 <see cref="Pick"/>。
    /// </summary>
    public HandleResult<ModuleRoutine> Place(int arm, string station, int slot)
    {
        lock (RoutineGate)
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
        lock (RoutineGate)
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

        if (_ledger is null || !_ledger.IsEnable)
        {
            return null;
        }

        if (_ledger.Get(station, slot) is null)
        {
            return HandleResult.Fail(ErrorCodes.WaferNoWafer, station, slot.ToString(CultureInfo.InvariantCulture));
        }

        var held = _ledger.Get(Name, arm);
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

        if (_ledger is null || !_ledger.IsEnable)
        {
            return null;
        }

        var armComponent = FindArm(arm);
        if (armComponent is not null && !armComponent.LedgerHasWafer)
        {
            return HandleResult.Fail(ErrorCodes.RobotArmNoWafer, Name, arm.ToString(CultureInfo.InvariantCulture));
        }

        var occupant = _ledger.Get(station, slot);
        if (occupant is not null)
        {
            return HandleResult.Fail(ErrorCodes.WaferSlotOccupied, station, slot.ToString(CultureInfo.InvariantCulture), occupant.WaferId);
        }

        return null;
    }

    /// <summary>
    /// 取片的手：站点在本机械手的示教点表里、这只手存在、这个站许它取片（示教点的 PickArms）
    /// </summary>
    private HandleResult? CheckPickArm(int arm, string station)
    {
        if (!TryGetTeachPoint(station, out var config))
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
    /// 放片的手：站点在本机械手的示教点表里、这只手存在、这个站许它放片（示教点的 PlaceArms）
    /// </summary>
    private HandleResult? CheckPlaceArm(int arm, string station)
    {
        if (!TryGetTeachPoint(station, out var config))
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
    /// 挂动作和记意图在同一把锁里：否则扫描线程可能在意图记上之前就把动作走完了，这一趟就不记账。
    /// </summary>
    private HandleResult<ModuleRoutine> StartTransfer(RobotAction action, int arm, string station, int slot)
    {
        if (!TryGetTeachPoint(station, out var config))
        {
            return Rejected(HandleResult.Fail(ErrorCodes.StationNotFound, Name, station));
        }

        bool pick = action == RobotAction.Pick;
        var noWafer = FindExpectedWafer(pick, arm, station, slot, out var expectedWaferId);
        if (noWafer is not null)
        {
            return Rejected(noWafer);
        }

        ModuleRoutine transfer;
        if (pick)
        {
            transfer = new ModuleRoutine(this, "Pick", _driver, () => PickTimeout);
            AddPickSteps(transfer, arm, config.Number, slot);
        }
        else
        {
            transfer = new ModuleRoutine(this, "Place", _driver, () => PlaceTimeout);
            AddPlaceSteps(transfer, arm, config.Number, slot);
        }

        return StartWithLedger(action, transfer, new TransferIntent(action, arm, station, slot, expectedWaferId), config);
    }

    /// <summary>
    /// 开着晶圆账时认定要搬的那一片（取片看站点这一槽、放片看这只手）：复用已有晶圆 Id，动作期间来源槽补成同样的业务片号也不会误移另一片的账；没片就拒
    /// </summary>
    private HandleResult? FindExpectedWafer(bool pick, int arm, string station, int slot, out Guid? expectedWaferId)
    {
        expectedWaferId = null;
        if (_ledger is null || !_ledger.IsEnable)
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

        var wafer = _ledger.Get(fromModule, fromSlot);
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
    /// 设备那几条后面再加上"等手指持片反馈对上""记账"两步再挂上：机型只给设备那几条，持片校验和记账平台加，记账通过才向搬运管理确认成功
    /// </summary>
    private HandleResult<ModuleRoutine> StartWithLedger(RobotAction action, ModuleRoutine transfer, TransferIntent intent, ITeachPoint config)
    {
        var armComponent = FindArm(intent.Arm);
        if (armComponent is not null && armComponent.IsWaferCheckEnabled)
        {
            AddSensorStep(transfer, armComponent, intent.Action == RobotAction.Pick);
        }

        transfer.Do("记账", () => CommitTransferLedger(intent) ?? HandleResult.Success());
        var started = Started(action, transfer);
        if (started.IsSuccess)
        {
            _currentTeachPoint = config;
            _transferIntent = intent;
        }

        return started;
    }

    /// <summary>
    /// 设备做完以后等手指的持片反馈对上（取完该有片、放完该没片）；EC WaferSensorTimeoutMs 内没对上，按读到的报
    /// </summary>
    private void AddSensorStep(ModuleRoutine transfer, IRobotArm sensorArm, bool expectWafer)
    {
        string arm = sensorArm.Number.ToString(CultureInfo.InvariantCulture);
        transfer.Until($"Arm{arm} 持片反馈对上", () => sensorArm.HasWafer == expectWafer, () => sensorArm.WaferSensorTimeoutMs,
            () => SensorMismatch(sensorArm.HasWafer, arm));
    }

    /// <summary>
    /// 持片反馈没对上报什么：读不到、该没片却有片、该有片却没片
    /// </summary>
    private HandleResult SensorMismatch(bool? sensed, string arm)
    {
        if (sensed is null)
        {
            return HandleResult.Fail(ErrorCodes.RobotArmSensorUnknown, Name, arm);
        }

        if (sensed.Value)
        {
            return HandleResult.Fail(ErrorCodes.RobotArmSensorHasWafer, Name, arm);
        }

        return HandleResult.Fail(ErrorCodes.RobotArmSensorNoWafer, Name, arm);
    }

    /// <summary>
    /// 取片那几条（只管设备做，持片检查、记账平台在后面加）：项目的机械手类用自己品牌的指令加 Command 步骤（超时用 EC PickTimeout）
    /// </summary>
    protected abstract void AddPickSteps(ModuleRoutine pick, int arm, int stationNumber, int slot);

    /// <summary>
    /// 放片那几条：同取片（超时用 EC PlaceTimeout）
    /// </summary>
    protected abstract void AddPlaceSteps(ModuleRoutine place, int arm, int stationNumber, int slot);

    /// <summary>
    /// 换片：默认不支持，返回 null（被拒）。机械手支持换片的，在机型里重写。
    /// </summary>
    public virtual ModuleRoutine? Swap(int pickArm, int placeArm, string station, int slot)
    {
        return null;
    }

    #endregion

    #region 分段取放（伸臂 → 停在站里等站点交接 → 缩臂）

    // 能分段取放的机械手类在类上声明 ITransferRobotStep（下面四个方法就是它的实现），再重写四个 AddExtend / RetractXxxSteps 加设备那几条。
    // 伸臂做完机械手停在"伸着"（PickExtended / PlaceExtended），手还在站里，状态表只收缩臂和急停；片在伸臂时认定，缩臂做完按它查持片、记账。

    /// <summary>
    /// 分段取片第一步：检查跟 Pick 一样；手伸到片下方停住，做完停在"取片伸着"
    /// </summary>
    public HandleResult<ModuleRoutine> ExtendPick(int arm, string station, int slot)
    {
        lock (RoutineGate)
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
    public HandleResult<ModuleRoutine> RetractPick(int arm, string station, int slot)
    {
        lock (RoutineGate)
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
    public HandleResult<ModuleRoutine> ExtendPlace(int arm, string station, int slot)
    {
        lock (RoutineGate)
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
    public HandleResult<ModuleRoutine> RetractPlace(int arm, string station, int slot)
    {
        lock (RoutineGate)
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
    private HandleResult<ModuleRoutine> StartExtend(RobotAction action, int arm, string station, int slot)
    {
        if (!TryGetTeachPoint(station, out var config))
        {
            return Rejected(HandleResult.Fail(ErrorCodes.StationNotFound, Name, station));
        }

        bool pick = action == RobotAction.ExtendPick;
        var noWafer = FindExpectedWafer(pick, arm, station, slot, out var expectedWaferId);
        if (noWafer is not null)
        {
            return Rejected(noWafer);
        }

        ModuleRoutine extend;
        var intentAction = RobotAction.Place;
        if (pick)
        {
            extend = new ModuleRoutine(this, "ExtendPick", _driver, () => PickTimeout);
            AddExtendPickSteps(extend, arm, config.Number, slot);
            intentAction = RobotAction.Pick;
        }
        else
        {
            extend = new ModuleRoutine(this, "ExtendPlace", _driver, () => PlaceTimeout);
            AddExtendPlaceSteps(extend, arm, config.Number, slot);
        }

        if (extend.StepCount == 0)
        {
            return Rejected(HandleResult.Fail(ErrorCodes.RobotHandoffUnsupported, Name));
        }

        var started = Started(action, extend);
        if (started.IsSuccess)
        {
            _currentTeachPoint = config;
            _transferIntent = new TransferIntent(intentAction, arm, station, slot, expectedWaferId);
        }

        return started;
    }

    /// <summary>
    /// 挂上缩臂：按伸臂时记下的那一趟查持片、记账；机械手类没给缩臂动作就拒（手还伸着，等人工）
    /// </summary>
    private HandleResult<ModuleRoutine> StartRetract(RobotAction action, TransferIntent intent)
    {
        if (!TryGetTeachPoint(intent.Station, out var config))
        {
            return Rejected(HandleResult.Fail(ErrorCodes.StationNotFound, Name, intent.Station));
        }

        ModuleRoutine retract;
        if (intent.Action == RobotAction.Pick)
        {
            retract = new ModuleRoutine(this, "RetractPick", _driver, () => PickTimeout);
            AddRetractPickSteps(retract, intent.Arm, config.Number, intent.Slot);
        }
        else
        {
            retract = new ModuleRoutine(this, "RetractPlace", _driver, () => PlaceTimeout);
            AddRetractPlaceSteps(retract, intent.Arm, config.Number, intent.Slot);
        }

        if (retract.StepCount == 0)
        {
            return Rejected(HandleResult.Fail(ErrorCodes.RobotHandoffUnsupported, Name));
        }

        return StartWithLedger(action, retract, intent, config);
    }

    /// <summary>
    /// 分段取片伸臂那几条：能分段取放的机械手类重写，发自己的伸臂指令（伸到片下方停住，不抬片）；默认一条没有，分段取放被拒
    /// </summary>
    protected virtual void AddExtendPickSteps(ModuleRoutine extend, int arm, int stationNumber, int slot)
    {
    }

    /// <summary>
    /// 分段取片缩臂那几条：站点已经把片交到手上，吸住确认后缩回；默认没有
    /// </summary>
    protected virtual void AddRetractPickSteps(ModuleRoutine retract, int arm, int stationNumber, int slot)
    {
    }

    /// <summary>
    /// 分段放片伸臂那几条：带着片伸到放片位停住，有吸附的手松开吸附；默认没有
    /// </summary>
    protected virtual void AddExtendPlaceSteps(ModuleRoutine extend, int arm, int stationNumber, int slot)
    {
    }

    /// <summary>
    /// 分段放片缩臂那几条：站点已经把片接走，空手缩回；默认没有
    /// </summary>
    protected virtual void AddRetractPlaceSteps(ModuleRoutine retract, int arm, int stationNumber, int slot)
    {
    }

    #endregion

    #region 扫片（可选能力 IWaferMap）

    // 能扫片的机械手类在类上声明 IWaferMap（下面的 Map / MapResults 就是它的实现），再重写 AddMapSteps 加扫片那几条，拿到槽图连同设备回的原文调 SaveMapResult 存起来。
    // machine.xml 开了 MapEnabled 的，它示教点表里的 LoadPort 由它扫（LoadPort 开机时按这个认），扫片用示教点的 Number；用哪只手扫是控制器示教的事，平台不选。

    private readonly object _mapGate = new();
    private readonly Dictionary<string, WaferMapResult> _mapResults = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 各站点最近一次扫到的结果（副本，槽图 + 设备回的原文）；开扫时先删掉这个站的旧结果
    /// </summary>
    public IReadOnlyDictionary<string, WaferMapResult> MapResults
    {
        get
        {
            lock (_mapGate)
            {
                return new Dictionary<string, WaferMapResult>(_mapResults, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// 扫这个站点：空闲、开了扫片、站点在示教点表里、手上全空才收；LoadPort 要它扫时由搬运管理排队，轮到了才调
    /// </summary>
    public HandleResult<ModuleRoutine> Map(string station)
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(RobotAction.Map) ?? CheckMapResource(station) ?? MapInterlock(station);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            if (!TryGetTeachPoint(station, out var config))
            {
                return Rejected(HandleResult.Fail(ErrorCodes.StationNotFound, Name, station));
            }

            var map = new ModuleRoutine(this, "Map", _driver, () => PickTimeout);
            AddMapSteps(map, station, config.Number);
            if (map.StepCount == 0)
            {
                return Rejected(HandleResult.Fail(ErrorCodes.RobotMapUnsupported, Name));
            }

            // 先删旧结果再挂动作：扫完存的一定是这一次的
            lock (_mapGate)
            {
                _mapResults.Remove(station);
            }

            var started = Started(RobotAction.Map, map);
            if (started.IsSuccess)
            {
                _currentTeachPoint = config;
            }

            return started;
        }
    }

    /// <summary>
    /// 扫片的资源：开了扫片、站点在示教点表里、手上全空——不知道扫片传感器装在哪只手上，拿着片去扫可能碰到盒子里的片
    /// </summary>
    private HandleResult? CheckMapResource(string station)
    {
        if (!MapEnabled)
        {
            return HandleResult.Fail(ErrorCodes.RobotMapUnsupported, Name);
        }

        if (!TryGetTeachPoint(station, out _))
        {
            return HandleResult.Fail(ErrorCodes.StationNotFound, Name, station);
        }

        if (_ledger is null || !_ledger.IsEnable)
        {
            return null;
        }

        foreach (var arm in Arms)
        {
            var held = _ledger.Get(Name, arm.Number);
            if (held is not null)
            {
                return HandleResult.Fail(ErrorCodes.RobotArmHasWafer, Name, arm.Number.ToString(CultureInfo.InvariantCulture), held.WaferId);
            }
        }

        return null;
    }

    /// <summary>
    /// 扫片前的互锁：平台不拦，机型要加条件就重写（先调 base）
    /// </summary>
    protected virtual HandleResult? MapInterlock(string station)
    {
        return null;
    }

    /// <summary>
    /// 扫片那几条：能扫片的机械手类重写，加自己的扫片、读结果指令，拿到槽图连同设备回的原文调 SaveMapResult；默认一条没有，扫片被拒。
    /// 超时默认按 EC PickTimeout 算，扫一趟比取片慢的先调 map.UseTimeout 换
    /// </summary>
    protected virtual void AddMapSteps(ModuleRoutine map, string station, int stationNumber)
    {
    }

    /// <summary>
    /// 存一个站点扫到的槽图（下标 0 是第 1 槽）和设备回的原文（各家自己的写法，平台只拿来写日志），给 MapResults 读
    /// </summary>
    protected void SaveMapResult(string station, IReadOnlyList<SlotState> slots, string raw)
    {
        lock (_mapGate)
        {
            _mapResults[station] = new WaferMapResult { Slots = slots.ToArray(), Raw = raw };
        }
    }

    #endregion

    #region PowerOn / PowerOff

    public HandleResult<ModuleRoutine> PowerOn()
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(RobotAction.PowerOn) ?? PowerOnInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var powerOn = new ModuleRoutine(this, "PowerOn", _driver, () => PowerTimeout);
            AddPowerOnSteps(powerOn);
            return Started(RobotAction.PowerOn, powerOn);
        }
    }

    public HandleResult<ModuleRoutine> PowerOff()
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(RobotAction.PowerOff) ?? PowerOffInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var powerOff = new ModuleRoutine(this, "PowerOff", _driver, () => PowerTimeout);
            AddPowerOffSteps(powerOff);
            return Started(RobotAction.PowerOff, powerOff);
        }
    }

    /// <summary>
    /// 上使能那几条：项目的机械手类给（超时用 EC PowerTimeout）
    /// </summary>
    protected abstract void AddPowerOnSteps(ModuleRoutine powerOn);

    /// <summary>
    /// 下使能那几条：项目的机械手类给（超时用 EC PowerTimeout）
    /// </summary>
    protected abstract void AddPowerOffSteps(ModuleRoutine powerOff);

    #endregion

    #region Reset / Abort

    /// <summary>
    /// 复位：清设备报错；只查状态，被拒带原因
    /// </summary>
    public override HandleResult<ModuleRoutine> Reset()
    {
        base.Reset();
        lock (RoutineGate)
        {
            var rejected = CheckState(RobotAction.Reset);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var reset = new ModuleRoutine(this, "Reset", _driver, () => ResetTimeout);
            AddResetSteps(reset);
            return Started(RobotAction.Reset, reset);
        }
    }

    /// <summary>
    /// 急停：只查状态、不看设备连没连上（见 <see cref="BaseModule.CheckState{TAction}"/>），被拒带原因
    /// </summary>
    public override HandleResult<ModuleRoutine> Abort()
    {
        base.Abort();
        lock (RoutineGate)
        {
            var rejected = CheckState(RobotAction.Abort);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var abort = new ModuleRoutine(this, "Abort", _driver, () => AbortTimeout);
            AddAbortSteps(abort);
            return Started(RobotAction.Abort, abort);
        }
    }

    /// <summary>
    /// 复位那几条（清设备报错）：项目的机械手类给（超时用 EC ResetTimeout）
    /// </summary>
    protected abstract void AddResetSteps(ModuleRoutine reset);

    /// <summary>
    /// 中止那几条（急停）：项目的机械手类给（超时用 EC AbortTimeout）；被顶掉的取放指令平台已经作废
    /// </summary>
    protected abstract void AddAbortSteps(ModuleRoutine abort);

    /// <summary>
    /// 这台机械手的解码器（拆帧、解报文），开机时交给通道
    /// </summary>
    protected abstract IFrameCodec CreateCodec();

    #endregion

    #region Interlock

    // 互锁：周围条件允不允许动。平台通过手指的统一持片检查契约查询反馈，机型重写加自己的条件（先调 base）。

    /// <summary>
    /// Home 前：启用持片检查的手，实际反馈跟晶圆账要对得上。账实不符先到账单调整页按实物改账再 Home，
    /// 不然回完原点接着按错账取放会碰片。反馈还没读到的手沿用现有规则，不拦 Home。
    /// </summary>
    protected virtual HandleResult? HomeInterlock()
    {
        if (_ledger is null || !_ledger.IsEnable)
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

    /// <summary>
    /// 手指的在位传感器跟晶圆账对一遍：配了传感器的手才比，配了读不到也算对不上（全部回片开始前用）
    /// </summary>
    public virtual HandleResult? CheckWaferSensors()
    {
        if (_ledger is null || !_ledger.IsEnable)
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
                return HandleResult.Fail(ErrorCodes.RobotArmSensorUnknown, Name, arm.Number.ToString(CultureInfo.InvariantCulture));
            }

            if (sensed.Value != arm.LedgerHasWafer)
            {
                return ArmSensorMismatch(arm.Number, sensed.Value);
            }
        }

        return null;
    }

    /// <summary>传感器跟账对不上的原因：传感器看到有片 / 没看到片各一个错误码。</summary>
    private HandleResult ArmSensorMismatch(int arm, bool sensed)
    {
        return HandleResult.Fail(sensed ? ErrorCodes.RobotArmSensorHasWafer : ErrorCodes.RobotArmSensorNoWafer,
            Name, arm.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>按手指号找手指组件；machine.xml 没配这只手为 null。</summary>
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
    protected override bool CanBeginAction => IsEnable && _driver is not null;

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

        var driver = _driver;
        if (driver is null || !driver.IsConnected)
        {
            return HandleResult.Fail(ErrorCodes.ModuleNotConnected, Name);
        }

        return null;
    }

    /// <summary>
    /// 动作停下（状态已由基类落好，做成的取放已经记了账）：清除交互意图（分段取放伸臂做完停在"伸着"的留着给缩臂记账），失败的动作报警。
    /// </summary>
    protected override void OnRoutineCompleted(ModuleRoutine routine)
    {
        if (State != RobotState.PickExtended && State != RobotState.PlaceExtended)
        {
            _transferIntent = null;
        }

        RaiseActionFailedAlarm(routine);
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
        var driver = _driver;
        if (driver is not null && driver.IsConnected && !string.IsNullOrEmpty(DeviceError))
        {
            RaiseAlarm(RobotDeviceAlarm);
        }
    }

    protected virtual void RaiseActionFailedAlarm(ModuleRoutine routine)
    {
        if (!routine.IsSuccess && routine.Code != ErrorCodes.Aborted)
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
        if (_ledger is null)
        {
            return null;
        }

        bool pick = intent.Action == RobotAction.Pick;
        string fromModule = pick ? intent.Station : Name;
        int fromSlot = pick ? intent.Slot : intent.Arm;
        string toModule = pick ? Name : intent.Station;
        int toSlot = pick ? intent.Arm : intent.Slot;
        if (intent.ExpectedWaferId is Guid expectedWaferId
            && _ledger.Move(fromModule, fromSlot, toModule, toSlot, expectedWaferId))
        {
            return null;
        }

        LogHelper.Error(this, $"{intent.Action} 动作已完成但账没记上：{fromModule}.{fromSlot:00} → {toModule}.{toSlot:00}，需确认实际片位");
        return HandleResult.Fail(ErrorCodes.RobotLedgerMoveFailed, Name,
            fromModule, fromSlot.ToString(CultureInfo.InvariantCulture),
            toModule, toSlot.ToString(CultureInfo.InvariantCulture));
    }

    #endregion
}
