using System.Globalization;
using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Database.DbProvider;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// 腔体模块基类：回零、复位、中止、按工艺配方做工艺、手动页的设备动作、状态推送平台都写好了，照 machine.xml 里这个腔体下挂的设备去发；
/// 机型只在步骤不一样时重写 AddXxxSteps，要加条件重写 XxxInterlock（先调 base）
/// </summary>
public abstract class BaseChamberModule : BaseTransferStationModule, IChamber
{
    /// <summary>
    /// 模块类型：腔体
    /// </summary>
    public override StationKind Kind => StationKind.Chamber;

    #region SV

    [VariableMark(VariableType.SV, ValueFormat.Int, description: "模块状态码")]
    public override int State { get; protected set; } = ModuleState.NotInit;

    private volatile string? _deviceError;

    /// <summary>
    /// 设备当前报错（错误码#内容）：平台腔体没有自己的控制器、不写它，机型接了腔体控制器的在查询或设备推送里写；无报错为 null
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "设备当前报错")]
    public string? DeviceError
    {
        get => _deviceError;
        protected set => _deviceError = value;
    }

    private volatile string? _recipe;

    /// <summary>
    /// 当前配方：最近一次发起成功的工艺配方名；还没做过工艺为 null
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "当前工艺配方名")]
    public string? Recipe
    {
        get => _recipe;
        private set => _recipe = value;
    }

    #endregion

    #region Machine

    [Machine("True", "Chamber", "是否启用本腔体 (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    [Machine("1", "Chamber", "片位数（腔体一般 1 片；晶圆账按它注册槽位）")]
    public override int SlotCount { get; set; } = 1;

    [Machine("", "Chamber", "这个腔体用的供液模块名（几个用逗号隔开，如 IPA,H2SO4；空 = 不看供液）：起工艺前都要就绪，EC SupplyCheckEnabled 关了不看")]
    public string Supply { get; set; } = string.Empty;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "60000", description: "Home 动作超时（回原点）")]
    public int HomeTimeout
    {
        get { return GetEcInt(nameof(HomeTimeout)); }
        set { SetEcInt(nameof(HomeTimeout), value); }
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
        @default: "60000", description: "设备手动动作超时（等轴、气缸这些设备做完的上限；点动是松手后等停下的上限）")]
    public int DeviceActionTimeout
    {
        get { return GetEcInt(nameof(DeviceActionTimeout)); }
        set { SetEcInt(nameof(DeviceActionTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Bool, @default: "True",
        description: "起工艺前看不看供液就绪（machine.xml Supply 配的供液模块）；False = 不看，调试时供液还没调好用")]
    public bool SupplyCheckEnabled
    {
        get { return !bool.TryParse(GetEcString(nameof(SupplyCheckEnabled)), out bool enabled) || enabled; }
        set { SetEc(nameof(SupplyCheckEnabled), value.ToString()); }
    }

    #endregion

    #region Alarm

    [Alarm("腔体设备报警", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Light,
        Description = "腔体控制器上报报错",
        Solution = "查询报错内容，排除故障后复位并重新回原点")]
    public string ChamberDeviceAlarm = nameof(ChamberDeviceAlarm);

    [Alarm("腔体受控停止", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Light,
        Description = "回原点、工艺等动作失败或超时，腔体进入错误状态",
        Solution = "先确认腔内实际片位与工艺是否跑完，再复位并重新回原点")]
    public string ControlledStopAlarm = nameof(ControlledStopAlarm);

    #endregion

    #region Element

    // 腔体里装的设备：元件初始化时照 machine.xml 认一次存起来（machine.xml 这个腔体节点下面挂的），节点名只标识实例，按接口认用途

    /// <summary>
    /// 腔门；没装为 null
    /// </summary>
    public IChamberDoor? Door { get; private set; }

    /// <summary>
    /// 接液杯，可以多层，按 machine.xml 的先后
    /// </summary>
    public IReadOnlyList<IChamberBowl> Bowls { get; private set; } = [];

    /// <summary>
    /// 卡盘；没装为 null
    /// </summary>
    public ISpinMotor? SpinMotor { get; private set; }

    /// <summary>
    /// 摆臂（它上面的 Lift、喷嘴在摆臂元件上），按 machine.xml 的先后
    /// </summary>
    public IReadOnlyList<ISwingArm> Arms { get; private set; } = [];

    /// <summary>
    /// 所有轴（卡盘、摆臂……），按 machine.xml 的先后（先父后子）
    /// </summary>
    public IReadOnlyList<IAxis> Axes { get; private set; } = [];

    /// <summary>
    /// 所有双作用气缸（门、Bowl、Lift……），按 machine.xml 的先后（先父后子）
    /// </summary>
    public IReadOnlyList<ICylinder> Cylinders { get; private set; } = [];

    /// <summary>
    /// 所有喷嘴，按 machine.xml 的先后（先父后子）
    /// </summary>
    public IReadOnlyList<INozzle> Nozzles { get; private set; } = [];

    /// <summary>
    /// 各条摆臂上的 Lift（没装 Lift 的摆臂不算），按摆臂的先后
    /// </summary>
    public IReadOnlyList<IChamberLift> Lifts { get; private set; } = [];

    /// <summary>
    /// 晶圆账：元件初始化时取一次；没配晶圆账、或腔体停用（不登记账）为 null
    /// </summary>
    private ILedgerManager? _ledger;

    /// <summary>
    /// 工艺配方库（开机 Bind 时取）：配方里的项在界面上叫什么要用；没装是 null
    /// </summary>
    private IRecipeElement? _recipes;

    /// <summary>
    /// 整机（开机 Bind 时取）：界面语言要用；没装是 null
    /// </summary>
    private ISystemModule? _system;

    /// <summary>
    /// 这个腔体用的供液模块：开机 Bind 时按 machine.xml Supply 配的名字取好
    /// </summary>
    private IReadOnlyList<ISupplyAccess> _supplies = [];

    #endregion

    #region 站点环

    protected override int StandbyState => ChamberState.Idle;

    /// <summary>
    /// 取放准备二：卡盘转到取放片示教位（卡盘 EC TransferPosition）、开腔门，都到位才算准备好（进 TransferReady），机械手才伸手进来。
    /// machine.xml 里卡盘、门都没配的照旧直接准备好。自动跑货、机械手手动页的取放都经搬运管理走到这里。转不到位、门开不了（卡盘没回零、到位超时、PLC 断了）
    /// 准备判失败、腔体落 Error，搬运那边按"第 2 步准备失败"收场；卡盘、腔门气缸自己报到位超时
    /// </summary>
    protected override ModuleRoutine? PrepareTransfer2Core(TransferContext context)
    {
        if (Door is null && SpinMotor is null)
        {
            return base.PrepareTransfer2Core(context);
        }

        // 指令由动作挂上之后在扫描里发，不会出现门开了、动作却没挂上没人等的情况
        var prepare = new ModuleRoutine(this, "PrepareTransfer2").LimitTo(() => DeviceActionTimeout, ErrorCodes.ChamberActionTimeout);
        AddPrepareTransferSteps(prepare);
        return BeginPrepareTransfer2(prepare);
    }

    /// <summary>
    /// 取放准备二：卡盘转到取放位、开门同时发（转位和开门互不碰，同时做省时间），都到位才算准备好。
    /// 卡盘先发：没回零（绝对定位发不出去）就直接失败，门不开。machine.xml 里没配卡盘或门的那一样就不动；机型做法不一样就重写
    /// </summary>
    protected virtual void AddPrepareTransferSteps(ModuleRoutine prepare)
    {
        var orders = new List<DeviceOrder>();
        var spin = SpinMotor;
        if (spin is not null)
        {
            orders.Add(new DeviceOrder(spin, ChamberDeviceAction.Move, () => spin.MoveTo(spin.TransferPosition)));
        }

        var door = Door;
        if (door is not null)
        {
            orders.Add(new DeviceOrder(door, ChamberDeviceAction.Up, door.Open));
        }

        prepare.Devices("卡盘转到取放位、开门", orders);
    }

    #endregion

    protected BaseChamberModule()
    {
        RegisterTransitions(ChamberStateTable.ToModuleTable());
    }

    #region 元件初始化

    /// <summary>
    /// 元件初始化（开机，不动硬件）：照 machine.xml 认一遍腔体里装的设备存起来（停用的腔体也认，推送、手动页照样看得到）；
    /// 启用的再登记晶圆账槽位，子元件交给基类递归。只能装一个的（腔门、卡盘）配重了开机就报
    /// </summary>
    public override bool InitElement()
    {
        #region 腔体里装的设备

        Door = FindSingleDevice<IChamberDoor>();
        SpinMotor = FindSingleDevice<ISpinMotor>();
        Bowls = Children.OfType<IChamberBowl>().ToList();
        Arms = Children.OfType<ISwingArm>().ToList();
        Axes = FindChildren<IAxis>();
        Cylinders = FindChildren<ICylinder>();
        Nozzles = FindChildren<INozzle>();
        Lifts = FindLifts();

        #endregion

        // 停用的腔体：不登记晶圆账，子元件也不初始化
        if (!IsEnable)
        {
            return true;
        }

        #region 晶圆账

        // 腔体在晶圆账里也是个位置：片停在腔里跟停在花篮里一样要有槽位
        _ledger = IocHelper.GetService<ILedgerManager>();
        _ledger?.RegisterLocation(Name, SlotCount);

        #endregion

        return base.InitElement();
    }

    /// <summary>
    /// 只能装一个的设备：不取"第一个"，配重了直接报错
    /// </summary>
    private T? FindSingleDevice<T>() where T : class, IDevice
    {
        var devices = Children.OfType<T>().ToList();
        if (devices.Count > 1)
        {
            throw new InvalidOperationException($"{FullPath}：只能装一个 {typeof(T).Name} 设备，实际配置了 {devices.Count} 个");
        }

        return devices.FirstOrDefault();
    }

    /// <summary>
    /// 摆臂上装了的 Lift
    /// </summary>
    private List<IChamberLift> FindLifts()
    {
        var lifts = new List<IChamberLift>();
        foreach (var arm in Arms)
        {
            var lift = arm.Lift;
            if (lift is not null)
            {
                lifts.Add(lift);
            }
        }

        return lifts;
    }

    /// <summary>
    /// 开机 Bind：取工艺配方库、整机，按 machine.xml Supply 取供液模块，开工片数读回今天那行接着数
    /// </summary>
    public override void Bind()
    {
        _recipes = IocHelper.GetService<IRecipeElement>();
        _system = IocHelper.GetService<ISystemModule>();
        BindSupplies();

        // 开工片数跟晶圆账一样是业务数据，走默认库；开机读回今天那行接着数
        _counter.Load(Name, XyzDb.DefaultName, this);
    }

    /// <summary>
    /// 按 machine.xml Supply 配的名字取到供液模块（不分大小写，重复的只取一次）：配了却没有这个模块，开机就报；停用的腔体不起工艺，不取
    /// </summary>
    private void BindSupplies()
    {
        var supplies = new List<ISupplyAccess>();
        if (IsEnable)
        {
            var all = IocHelper.GetServices<ISupplyAccess>();
            foreach (string name in Supply.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var supply = all.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                if (supply is null)
                {
                    throw new InvalidOperationException($"machine.xml 节点 {FullPath} 的 Supply 配了 {name}，但没有这个供液模块（没配或停用了）。");
                }

                if (!supplies.Contains(supply))
                {
                    supplies.Add(supply);
                }
            }
        }

        _supplies = supplies;
    }

    #endregion

    #region 状态发布

    private ChamberDto? _lastPublishedState;

    /// <summary>
    /// 当前状态快照，状态发布与 GetState 查询共用
    /// </summary>
    public ChamberDto CreateStateDto()
    {
        var dto = new ChamberDto
        {
            Name = Name,
            State = State,
            Mode = Mode,
            IsEnable = IsEnable,
            Recipe = Recipe,
            SlotCount = SlotCount,
            Slots = CreateSlotDtos(),
        };

        if (IsEnable)
        {
            dto.DeviceError = DeviceError;
        }

        return dto;
    }

    /// <summary>
    /// 片位表：按 SlotCount 逐位查晶圆账；账上没注册这个腔体（停用、或 machine.xml 没配晶圆账）时全给空
    /// </summary>
    private List<ChamberSlotDto> CreateSlotDtos()
    {
        IReadOnlyList<WaferInfo?> wafers = [];
        if (_ledger is not null)
        {
            wafers = _ledger.GetSlots(Name);
        }

        var slots = new List<ChamberSlotDto>();
        for (int slot = 1; slot <= SlotCount; slot++)
        {
            WaferInfo? wafer = null;
            if (slot <= wafers.Count)
            {
                wafer = wafers[slot - 1];
            }

            slots.Add(new ChamberSlotDto
            {
                Slot = slot,
                State = ToSlotState(wafer),
                WaferId = wafer?.WaferId,
                SourceLoadPort = wafer?.SourceLoadPort,
                SourceSlot = wafer?.SourceSlot ?? 0,
            });
        }

        return slots;
    }

    /// <summary>
    /// 账上的片转片位状态：没片为空，有片按它的工艺状态
    /// </summary>
    private static ChamberSlotState ToSlotState(WaferInfo? wafer)
    {
        if (wafer is null)
        {
            return ChamberSlotState.Empty;
        }

        switch (wafer.ProcessState)
        {
            case WaferProcessState.InProcess:
                return ChamberSlotState.InProcess;
            case WaferProcessState.Completed:
                return ChamberSlotState.Completed;
            case WaferProcessState.Failed:
                return ChamberSlotState.Failed;
            case WaferProcessState.Aborted:
                return ChamberSlotState.Aborted;
            default:
                return ChamberSlotState.Idle;
        }
    }

    /// <summary>
    /// 发布当前状态（扫描周期调，状态环改完状态也会立即调）：首次发布，之后只在状态变化时发布。
    /// 模块状态和设备状态各推各的，都留存（供界面晚订阅或重连时补发）
    /// </summary>
    protected override void PublishState()
    {
        var dto = CreateStateDto();
        if (dto.HasStateChanged(_lastPublishedState))
        {
            _lastPublishedState = dto;
            RemoteEventBus.Send(dto, Name);
        }

        PublishDeviceData();
    }

    #endregion

    #region 设备状态推送（手动页的轴页签、气缸表、三维图）

    /// <summary>
    /// 位置、速度、摆幅推几位小数：编码器在这一位以下抖不会一直推
    /// </summary>
    private const int PushDecimals = 3;

    private volatile ChamberDeviceDataDto? _lastPublishedDeviceData;

    /// <summary>
    /// 设备状态快照，结构跟 machine.xml 一样：门、Bowl、卡盘、各条摆臂（摆臂下面是它的 Lift 和喷嘴）
    /// </summary>
    public ChamberDeviceDataDto CreateDeviceDataDto()
    {
        var dto = new ChamberDeviceDataDto { Module = Name };
        var door = Door;
        if (door is not null)
        {
            dto.Door = CylinderDtoOf(door);
        }

        foreach (var bowl in Bowls)
        {
            dto.Bowls.Add(CylinderDtoOf(bowl));
        }

        var spin = SpinMotor;
        if (spin is not null)
        {
            dto.Spin = AxisDtoOf<ChamberAxisDto>(spin);
        }

        foreach (var arm in Arms)
        {
            var armDto = AxisDtoOf<ChamberArmDto>(arm);
            armDto.Reach = RoundForPush(arm.Reach);
            armDto.EdgeReach = RoundForPush(arm.EdgeReach);
            var lift = arm.Lift;
            if (lift is not null)
            {
                armDto.Lift = CylinderDtoOf(lift);
            }

            foreach (var nozzle in arm.Nozzles)
            {
                armDto.Nozzles.Add(new ChamberNozzleDto
                {
                    Path = nozzle.FullPath,
                    Chemical = nozzle.Chemical,
                    IsOn = nozzle.IsOn,
                });
            }

            dto.Arms.Add(armDto);
        }

        return dto;
    }

    /// <summary>
    /// 设备状态有变化才推；token 是模块名，跟 ChamberDto 类型不同、互不覆盖
    /// </summary>
    private void PublishDeviceData()
    {
        var data = CreateDeviceDataDto();
        if (!data.HasStateChanged(_lastPublishedDeviceData))
        {
            return;
        }

        _lastPublishedDeviceData = data;
        RemoteEventBus.Send(data, Name);
    }

    /// <summary>
    /// 一根轴的位置、速度、五盏灯（卡盘、摆臂共用这一段，各自的再另外填）
    /// </summary>
    private static T AxisDtoOf<T>(IAxis axis) where T : ChamberAxisDto, new()
    {
        return new T
        {
            Path = axis.FullPath,
            HasPlcData = axis.HasPlcData,
            CurrentPosition = RoundForPush(axis.CurrentPosition),
            CurrentSpeed = RoundForPush(axis.CurrentSpeed),
            IsServoOn = axis.IsServoOn,
            IsHomed = axis.IsHomed,
            IsBusy = axis.IsBusy,
            IsInPosition = axis.IsInPosition,
            IsError = axis.IsError,
        };
    }

    /// <summary>
    /// 一个气缸推给界面的位置：升到位 / 降到位 / 未知
    /// </summary>
    private static ChamberCylinderDto CylinderDtoOf(ICylinder cylinder)
    {
        CylinderPosition position;
        switch (cylinder.Position)
        {
            case TwoStatePosition.Opened:
                position = CylinderPosition.Opened;
                break;
            case TwoStatePosition.Closed:
                position = CylinderPosition.Closed;
                break;
            default:
                position = CylinderPosition.Unknown;
                break;
        }

        return new ChamberCylinderDto { Path = cylinder.FullPath, Position = position };
    }

    /// <summary>
    /// 按推的位数四舍五入（-0 记成 0）；不是有限数记 0
    /// </summary>
    private static double RoundForPush(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        double rounded = Math.Round(value, PushDecimals, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            return 0;
        }

        return rounded;
    }

    #endregion

    #region IChamber 契约

    /// <summary>
    /// 模块初始化 = Home
    /// </summary>
    public override HandleResult<ModuleRoutine> InitModule()
    {
        return Home();
    }

    /// <summary>
    /// 按节点名找摆臂（配方里选的就是节点名，如 "Arm1"，忽略大小写）；没有为 null
    /// </summary>
    public ISwingArm? FindArm(string name)
    {
        return Arms.FirstOrDefault(arm => string.Equals(arm.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按全路径找轴（手动页按推送里的 Path 发，忽略大小写）；没有为 null
    /// </summary>
    private IAxis? FindAxis(string path)
    {
        return Axes.FirstOrDefault(axis => string.Equals(axis.FullPath, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按全路径找气缸；没有为 null
    /// </summary>
    private ICylinder? FindCylinder(string path)
    {
        return Cylinders.FirstOrDefault(cylinder => string.Equals(cylinder.FullPath, path, StringComparison.OrdinalIgnoreCase));
    }

    #region 动作入口：状态 → 资源 → 互锁

    // 跟 LoadPort、机械手同一套：每个动作的入口在模块锁里按 状态 → 资源 → 互锁 查（三项怎么分见 BaseModule"动作检查"区），
    // 哪项不过就带原因返回、腔体不动，都过了才挂上动作。每个动作的步骤平台给默认（照 machine.xml 里挂的设备发），
    // 机型步骤不一样才重写 AddXxxSteps，要加条件重写 XxxInterlock（先调 base）。

    /// <summary>
    /// 检查都过了，挂上设备动作；挂不上（检查和挂在同一把锁里，正常不会）回笼统的被拒 + 当前状态
    /// </summary>
    private HandleResult<ModuleRoutine> Started(ChamberAction action, ModuleRoutine routine)
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
    /// Home：整腔回零。调用方：手动页、模块初始化（整机初始化）
    /// </summary>
    public HandleResult<ModuleRoutine> Home()
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(ChamberAction.Home) ?? HomeInterlock();
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var home = new ModuleRoutine(this, "Home").LimitTo(() => HomeTimeout, ErrorCodes.ChamberActionTimeout);
            AddHomeSteps(home);
            return Started(ChamberAction.Home, home);
        }
    }

    /// <summary>
    /// 回零：喷嘴全关 → 卡盘停转 → Lift 升 → 摆臂、卡盘（和别的轴）回零 → Bowl 降，门不动（门归站点交互环管）。
    /// Lift 先升起来，摆臂回零时才不会刮到 Bowl 壁；卡盘要先停稳再回零，回过零取放准备才能按绝对位置转到取放位。
    /// machine.xml 里没配的设备那一步就不加；机型的先后不一样就重写
    /// </summary>
    protected virtual void AddHomeSteps(ModuleRoutine home)
    {
        home.Devices("关喷嘴", Nozzles, ChamberDeviceAction.ValveOff);
        home.Devices("卡盘停转", SpinMotor, ChamberDeviceAction.Stop);
        home.Devices("Lift 升", Lifts, ChamberDeviceAction.Up);
        home.Devices("轴回零", Axes, ChamberDeviceAction.Home);
        home.Devices("Bowl 降", Bowls, ChamberDeviceAction.Down);
    }

    #endregion

    #region 工艺

    /// <summary>
    /// 起工艺：手动页、Job（站内任务）走同一个口子，检查跟 <see cref="CheckProcess"/> 一样、跟挂动作在同一把锁里。
    /// 挂上了就记下配方名（SV Recipe）、开工计数加一、账上的片标成加工中——扫描线程拿不到锁就推进不了，不会出现工艺做完了账才标加工中的倒挂
    /// </summary>
    public HandleResult<ModuleRoutine> StartProcess(ProcessRequest request)
    {
        lock (RoutineGate)
        {
            var rejected = CheckProcess(request);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var started = Started(ChamberAction.Process, CreateProcessRoutine(request));
            var routine = started.Result;
            if (!started.IsSuccess || routine is null)
            {
                return started;
            }

            _process = request;
            _processRoutine = routine;
            _counter.Started();
            Recipe = request.RecipeName.Trim();
            if (_ledger is not null && _ledger.HasWafer(Name, request.Slot))
            {
                _ledger.SetProcessState(Name, request.Slot, WaferProcessState.InProcess);
            }

            return started;
        }
    }

    /// <summary>
    /// 起工艺能不能做（只查不动）：状态 → 资源（配方、片）→ 互锁（供液）。Job 的站内任务、手动起工艺都可以先问
    /// </summary>
    public HandleResult? CheckProcess(ProcessRequest request)
    {
        lock (RoutineGate)
        {
            return CheckState(ChamberAction.Process) ?? CheckProcessResource(request) ?? ProcessInterlock(request);
        }
    }

    /// <summary>
    /// 工艺的资源检查：要有配方内容（快照）才知道怎么做；配方的 Type 跟这个腔体一样、选的摆臂和药液这个腔体有（照名字找，跟执行时一样）；
    /// 槽号在范围里；指定了片的（Job 起的）腔里得正好是那一片，换过片、片没到都不起
    /// </summary>
    private HandleResult? CheckProcessResource(ProcessRequest request)
    {
        string recipeName = request.RecipeName.Trim();
        if (recipeName.Length == 0)
        {
            return HandleResult.Fail(ErrorCodes.RecipeRequired, Name);
        }

        // 没装工艺配方库时只有名字、没有内容，起不了
        var recipe = request.Recipe;
        if (recipe is null)
        {
            return HandleResult.Fail(ErrorCodes.ChamberRecipeStepsMissing, Name, recipeName);
        }

        var mismatch = FindRecipeMismatch(recipe);
        if (mismatch is not null)
        {
            var found = mismatch.Value;
            return HandleResult.Fail(ErrorCodes.ChamberRecipeOptionMissing, Name, recipeName, found.Field, found.Value);
        }

        string slot = request.Slot.ToString(CultureInfo.InvariantCulture);
        if (request.Slot < 1 || request.Slot > SlotCount)
        {
            return HandleResult.Fail(ErrorCodes.ChamberWaferMismatch, Name, slot);
        }

        var expected = request.WaferId;
        if (expected is not null)
        {
            var wafer = _ledger?.Get(Name, request.Slot);
            if (wafer is null || wafer.Id != expected.Value)
            {
                return HandleResult.Fail(ErrorCodes.ChamberWaferMismatch, Name, slot);
            }
        }

        return null;
    }

    /// <summary>
    /// 工艺：按请求里的配方快照一步一步转、摆、喷（<see cref="ChamberProcessRoutine"/>，上限是配方头的超时时间）。
    /// 检查过了才调，配方快照一定有；机型做法不一样就重写
    /// </summary>
    protected virtual ModuleRoutine CreateProcessRoutine(ProcessRequest request)
    {
        var recipe = request.Recipe ?? throw new InvalidOperationException($"{Name}：起工艺的请求没带配方快照（资源检查应该先挡下）");
        return new ChamberProcessRoutine(this, request.RecipeName, recipe);
    }

    #endregion

    #region 设备手动动作（手动页：轴回零 / 移动 / 步进 / 点动 / 停止 / 复位，气缸升 / 降）

    // 先认设备、查参数（找不到、参数不对直接回，跟腔体状态无关），再在锁里查 状态 → 互锁；都过了当场发指令：
    // 发不出去（PLC 没连、轴没回零……）直接带原因回，模块状态不动、也不报警；发出去了才挂上等它做完的动作，
    // 执行中落 ChamberState.Manual，做完回到原来的状态。停止不看忙不忙、不挂动作，发出去就回。
    // 不带参数的（回零、复位、升降）跟整腔动作一样经 DeviceActions 发，带参数的（移动、步进）自己给怎么发。

    /// <summary>
    /// 轴回零
    /// </summary>
    public HandleResult<ModuleRoutine> AxisHome(string path)
    {
        var axis = FindAxis(path);
        if (axis is null)
        {
            return Rejected(DeviceNotFound(path));
        }

        return StartDeviceAction(axis, path, ChamberDeviceAction.Home);
    }

    /// <summary>
    /// 轴走到绝对位置；speed 为 0 按这根轴的 EC MoveSpeed
    /// </summary>
    public HandleResult<ModuleRoutine> AxisMove(string path, double position, double speed)
    {
        var axis = FindAxis(path);
        if (axis is null)
        {
            return Rejected(DeviceNotFound(path));
        }

        if (!double.IsFinite(position) || !IsSpeed(speed))
        {
            return Rejected(DeviceArgsInvalid(path, ChamberDeviceAction.Move));
        }

        return StartDeviceAction(axis, path, ChamberDeviceAction.Move, () => axis.MoveTo(position, SpeedOrDefault(speed)));
    }

    /// <summary>
    /// 轴走一段（正负是方向，不能是 0）；speed 为 0 按这根轴的 EC MoveSpeed
    /// </summary>
    public HandleResult<ModuleRoutine> AxisStep(string path, double distance, double speed)
    {
        var axis = FindAxis(path);
        if (axis is null)
        {
            return Rejected(DeviceNotFound(path));
        }

        if (!double.IsFinite(distance) || distance == 0 || !IsSpeed(speed))
        {
            return Rejected(DeviceArgsInvalid(path, ChamberDeviceAction.Step));
        }

        return StartDeviceAction(axis, path, ChamberDeviceAction.Step, () => axis.MoveBy(distance, SpeedOrDefault(speed)));
    }

    /// <summary>
    /// 轴复位清错
    /// </summary>
    public HandleResult<ModuleRoutine> AxisReset(string path)
    {
        var axis = FindAxis(path);
        if (axis is null)
        {
            return Rejected(DeviceNotFound(path));
        }

        return StartDeviceAction(axis, path, ChamberDeviceAction.Reset);
    }

    /// <summary>
    /// 气缸升（true，开侧）或降
    /// </summary>
    public HandleResult<ModuleRoutine> MoveCylinder(string path, bool up)
    {
        var cylinder = FindCylinder(path);
        if (cylinder is null)
        {
            return Rejected(DeviceNotFound(path));
        }

        if (up)
        {
            return StartDeviceAction(cylinder, path, ChamberDeviceAction.Up);
        }

        return StartDeviceAction(cylinder, path, ChamberDeviceAction.Down);
    }

    /// <summary>
    /// 轴点动（速度正负是方向，不能是 0）：按下发一次就回，返回的动作等松手；一直走到松手发 <see cref="AxisStop"/>
    /// </summary>
    public HandleResult<ModuleRoutine> AxisJog(string path, double speed)
    {
        var axis = FindAxis(path);
        if (axis is null)
        {
            return Rejected(DeviceNotFound(path));
        }

        if (!double.IsFinite(speed) || speed == 0)
        {
            return Rejected(DeviceArgsInvalid(path, ChamberDeviceAction.Jog));
        }

        lock (RoutineGate)
        {
            var rejected = CheckState(ChamberAction.Manual) ?? DeviceActionInterlock(axis, ChamberDeviceAction.Jog);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            if (!axis.Jog(speed))
            {
                return Rejected(DeviceCommandRejected(path, ChamberDeviceAction.Jog));
            }

            // 一直走到松手（界面发停止），再等轴停下
            var jog = new ModuleRoutine(this, $"{axis.FullPath} {ChamberDeviceAction.Jog}");
            jog.Add("按住", null, HoldJog);
            jog.AwaitSent("等停下", axis, ChamberDeviceAction.Jog, () => DeviceActionTimeout);
            _jogAxis = axis;
            _jogReleased = false;
            var started = Started(ChamberAction.Manual, jog);
            if (!started.IsSuccess)
            {
                // 检查跟挂在同一把锁里，挂不上只是防万一；点动已经发出去了，先停下来
                axis.Stop();
                return started;
            }

            _jogRoutine = jog;
            return started;
        }
    }

    /// <summary>
    /// 按住期间每拍：界面松手了这一步就过；轴报失败先停再判失败
    /// </summary>
    private bool HoldJog(RoutineStep step)
    {
        var axis = _jogAxis;
        if (axis is null || _jogReleased)
        {
            return true;
        }

        if (axis.ActionState == ActionState.Failed)
        {
            axis.Stop();
            step.Fail(ErrorCodes.ChamberDeviceActionFailed, $"{axis.FullPath} 点动中轴报失败（设备报错）",
                axis.FullPath, nameof(ChamberDeviceAction.Jog));
        }

        return false;
    }

    /// <summary>
    /// 正在点动这根轴：点动的动作还挂着、还在走
    /// </summary>
    private bool IsJogging(IAxis axis)
    {
        var jog = _jogRoutine;
        return jog is not null && ReferenceEquals(CurrentRoutine, jog) && ReferenceEquals(_jogAxis, axis) && !jog.IsTerminal;
    }

    /// <summary>
    /// 轴停止：不看腔体忙不忙、不挂动作，发出去就回；正按着的点动算松手（之后等轴停下就退出"手动中"）
    /// </summary>
    public HandleResult AxisStop(string path)
    {
        var axis = FindAxis(path);
        if (axis is null)
        {
            return DeviceNotFound(path);
        }

        if (!axis.Stop())
        {
            return DeviceCommandRejected(path, ChamberDeviceAction.Stop);
        }

        if (IsJogging(axis))
        {
            _jogReleased = true;
        }

        return HandleResult.Success();
    }

    /// <summary>
    /// 不带参数的设备动作：经 DeviceActions 发（跟整腔动作同一处）
    /// </summary>
    private HandleResult<ModuleRoutine> StartDeviceAction(IDevice device, string path, ChamberDeviceAction action)
    {
        return StartDeviceAction(device, path, action, () => DeviceActions.Send(device, action));
    }

    /// <summary>
    /// 锁里查 状态 → 互锁，过了当场发指令，发出去了挂上等设备做完的动作（设备回到 Idle 算被中止，EC DeviceActionTimeout 兜底）
    /// </summary>
    private HandleResult<ModuleRoutine> StartDeviceAction(IDevice device, string path, ChamberDeviceAction action, Func<bool> send)
    {
        lock (RoutineGate)
        {
            var rejected = CheckState(ChamberAction.Manual) ?? DeviceActionInterlock(device, action);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            if (!send())
            {
                return Rejected(DeviceCommandRejected(path, action));
            }

            var manual = new ModuleRoutine(this, $"{device.FullPath} {action}");
            manual.AwaitSent("等做完", device, action, () => DeviceActionTimeout);
            return Started(ChamberAction.Manual, manual);
        }
    }

    /// <summary>
    /// 这个路径不是这种设备（轴的动作给了气缸路径、腔体下没有它）
    /// </summary>
    private HandleResult DeviceNotFound(string path)
    {
        return HandleResult.Fail(ErrorCodes.ChamberDeviceNotFound, Name, path);
    }

    /// <summary>
    /// 参数不对：不是有限数、速度是负的、点动速度或步距是 0
    /// </summary>
    private static HandleResult DeviceArgsInvalid(string path, ChamberDeviceAction action)
    {
        return HandleResult.Fail(ErrorCodes.ChamberDeviceArgsInvalid, path, action.ToString());
    }

    /// <summary>
    /// 指令没发出去（PLC 没连、轴没回零 / 没使能 / 正忙……）
    /// </summary>
    private static HandleResult DeviceCommandRejected(string path, ChamberDeviceAction action)
    {
        return HandleResult.Fail(ErrorCodes.ChamberDeviceCommandRejected, path, action.ToString());
    }

    /// <summary>
    /// 速度：0 = 用 EC 默认值，不能是负的、不能不是有限数
    /// </summary>
    private static bool IsSpeed(double speed)
    {
        return double.IsFinite(speed) && speed >= 0;
    }

    /// <summary>
    /// 给轴的速度：0 换成 null（轴按自己的 EC MoveSpeed 走）
    /// </summary>
    private static double? SpeedOrDefault(double speed)
    {
        if (speed > 0)
        {
            return speed;
        }

        return null;
    }

    #endregion

    #region Reset / Abort

    /// <summary>
    /// 复位：先清报警、复位子元件（轴在这一步发驱动器复位），再挂上等设备复位做完的动作；只查状态，被拒带原因。
    /// 卡在交互环里（取放片失败停在 Transferring）也能复位，落 Idle 等于强制脱离这一轮交互
    /// </summary>
    public override HandleResult<ModuleRoutine> Reset()
    {
        base.Reset();
        lock (RoutineGate)
        {
            var rejected = CheckState(ChamberAction.Reset);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var reset = new ModuleRoutine(this, "Reset").LimitTo(() => ResetTimeout, ErrorCodes.ChamberActionTimeout);
            AddResetSteps(reset);
            return Started(ChamberAction.Reset, reset);
        }
    }

    /// <summary>
    /// 中止（急停）：先中止子元件（轴在这一步发停止），再挂上设备中止；能顶替在途动作，不清报警。
    /// 只查停用和状态表、不看设备连没连上（见 <see cref="BaseModule.CheckState{TAction}"/>），被拒带原因
    /// </summary>
    public override HandleResult<ModuleRoutine> Abort()
    {
        base.Abort();
        lock (RoutineGate)
        {
            var rejected = CheckState(ChamberAction.Abort);
            if (rejected is not null)
            {
                return Rejected(rejected);
            }

            var abort = new ModuleRoutine(this, "Abort").LimitTo(() => AbortTimeout, ErrorCodes.ChamberActionTimeout);
            AddAbortSteps(abort);
            return Started(ChamberAction.Abort, abort);
        }
    }

    /// <summary>
    /// 复位：清错指令在这之前已经发了——腔体 Reset 先走元件基类的复位，一路递归到每根轴，轴自己发驱动器复位；
    /// 这里等所有轴把复位做完。气缸、喷嘴的复位只是清报警，不用等。机型不一样就重写
    /// </summary>
    protected virtual void AddResetSteps(ModuleRoutine reset)
    {
        reset.Await("等轴复位", Axes, ChamberDeviceAction.Reset);
    }

    /// <summary>
    /// 中止（急停）：轴的停止在这之前已经发了——腔体 Abort 先走元件基类的中止，一路递归到每根轴，轴自己发停止；
    /// 气缸、阀的中止只是不再等到位、输出保持原样，所以这里把喷嘴全部停液（别中止了还在喷），再等所有轴停下。机型不一样就重写
    /// </summary>
    protected virtual void AddAbortSteps(ModuleRoutine abort)
    {
        abort.Devices("喷嘴停液", Nozzles, ChamberDeviceAction.ValveOff);
        abort.Await("等轴停下", Axes, ChamberDeviceAction.Stop);
    }

    #endregion

    #region Interlock

    // 互锁：周围条件允不允许动。平台给默认，机型有别的条件重写、先调 base，用机型自己的错误码。

    /// <summary>
    /// 回零前：平台不拦
    /// </summary>
    protected virtual HandleResult? HomeInterlock()
    {
        return null;
    }

    /// <summary>
    /// 起工艺前：这个腔体用的供液模块都得就绪。只在开工这一下看，喷的中途不再看；EC SupplyCheckEnabled 关了不看（调试时供液还没调好）
    /// </summary>
    protected virtual HandleResult? ProcessInterlock(ProcessRequest request)
    {
        if (!SupplyCheckEnabled)
        {
            return null;
        }

        foreach (var supply in _supplies)
        {
            if (!supply.IsReady)
            {
                return HandleResult.Fail(ErrorCodes.ChamberSupplyNotReady, Name, request.RecipeName.Trim(), supply.Name);
            }
        }

        return null;
    }

    /// <summary>
    /// 手动页的设备动作前：平台还没做（比如 Bowl 升着不许摆臂，用户押后了），操作员自己看着点
    /// </summary>
    protected virtual HandleResult? DeviceActionInterlock(IDevice device, ChamberDeviceAction action)
    {
        return null;
    }

    #endregion

    #endregion

    #endregion

    /// <summary>
    /// 装机停用的腔体不发动作（腔体不持驱动，能不能发只看这一条）
    /// </summary>
    protected override bool CanBeginAction => IsEnable;

    /// <summary>
    /// 动作停下（状态已由基类落好）：失败的动作报警；工艺做完把账上的片标成完成 / 失败 / 中止；点动停下了不再认续
    /// </summary>
    protected override void OnRoutineCompleted(ModuleRoutine routine)
    {
        RaiseActionFailedAlarm(routine);
        FinishProcess(routine);
        if (ReferenceEquals(routine, _jogRoutine))
        {
            _jogRoutine = null;
            _jogAxis = null;
        }
    }

    #region 点动（按住动、松手停）

    // 点动挂的动作要跟界面的续、松手对上：续、松手在 RPC 线程，扫描在模块线程，两边只碰这几个字段（一个腔体同一时间只有一个动作）

    /// <summary>
    /// 正在点动的那个动作；没在点动为 null
    /// </summary>
    private volatile ModuleRoutine? _jogRoutine;

    /// <summary>
    /// 点动的轴
    /// </summary>
    private volatile IAxis? _jogAxis;

    /// <summary>
    /// 已经松手（界面发了停止）
    /// </summary>
    private volatile bool _jogReleased;

    #endregion

    #region 工艺计数和进度（IProcessStation：Job 和手动起工艺走同一个口子）

    /// <summary>
    /// 正在跑的工艺请求；没在跑为 null
    /// </summary>
    private volatile ProcessRequest? _process;

    /// <summary>
    /// 正在跑的工艺动作：只在模块锁里改，进度在别的线程读；没在跑为 null
    /// </summary>
    private volatile ModuleRoutine? _processRoutine;

    /// <summary>
    /// 开工计数：今天几片、累计几片、最近一次开工时间（按天存库）
    /// </summary>
    private readonly ProcessCounter _counter = new();

    /// <summary>
    /// 正在跑的工艺请求；没在跑为 null
    /// </summary>
    public ProcessRequest? CurrentProcess => _process;

    /// <summary>
    /// 今天开工了几片（中止、失败的也算）
    /// </summary>
    public int TodayWafers => _counter.Today;

    /// <summary>
    /// 累计开工了几片
    /// </summary>
    public long TotalWafers => _counter.Total;

    /// <summary>
    /// 最近一次开工的时间戳（开机以来的计时，改系统时间不影响）；这次开机还没开过工为空
    /// </summary>
    public long? LastStartedTimestamp => _counter.LastStartedTimestamp;

    /// <summary>
    /// 当前工艺进度 0~100：平台的工艺动作按配方每步时间算；没在做工艺、机型自己造的工艺动作为空
    /// </summary>
    public int? ProcessPercent => (_processRoutine as ChamberProcessRoutine)?.Percent;

    /// <summary>
    /// 这个配方在这个腔体上跑不跑得了：配方头的 Type 要跟这个腔体的 Type 一样（不分大小写）；
    /// 每一步选的摆臂、药液照名字在这个腔体里找（跟执行时同一段，见 ChamberProcessRoutine.FindDevices）。
    /// 对得上返回 null；对不上回第一处的字段（按界面语言的名字，没装工艺配方库就用字段名）和值。
    /// 起工艺前、流程配方保存、Job 挑腔体时查，免得做到一半才发现
    /// </summary>
    public (string Field, string Value)? FindRecipeMismatch(RecipeData recipe)
    {
        if (!string.Equals(recipe.Type.Trim(), Type.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (FieldText(RecipeHead.TypeKey), recipe.Type.Trim());
        }

        foreach (var step in recipe.Steps)
        {
            var missing = ChamberProcessRoutine.FindDevices(this, step, out _, out _);
            if (missing is not null)
            {
                return (FieldText(missing.Value.Key), missing.Value.Value);
            }
        }

        return null;
    }

    /// <summary>
    /// 配方里一项在界面上叫什么（按界面语言，步骤、配方头的字段表里找）；没装工艺配方库就用字段名
    /// </summary>
    private string FieldText(string key)
    {
        var library = _recipes;
        if (library is null)
        {
            return key;
        }

        var field = library.Fields.Concat(library.HeadFields).FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            return key;
        }

        return field.DisplayText(_system?.Language);
    }

    /// <summary>
    /// 工艺动作停下（在模块锁里）：账上的片按结果标完成 / 失败 / 中止；不是工艺动作不管
    /// </summary>
    private void FinishProcess(ModuleRoutine routine)
    {
        if (!ReferenceEquals(routine, _processRoutine))
        {
            return;
        }

        var request = _process;
        _process = null;
        _processRoutine = null;
        bool aborted = routine.State == RoutineState.Aborted;

        // 工艺做到一半失败、超时：先把喷嘴停了，别一直喷（中止由中止动作自己停液）
        if (!routine.IsSuccess && !aborted)
        {
            foreach (var nozzle in Nozzles)
            {
                nozzle.Stop();
            }
        }

        if (request is null || _ledger is null || !_ledger.HasWafer(Name, request.Slot))
        {
            return;
        }

        var state = WaferProcessState.Failed;
        if (routine.IsSuccess)
        {
            state = WaferProcessState.Completed;
        }
        else if (aborted)
        {
            state = WaferProcessState.Aborted;
        }

        _ledger.SetProcessState(Name, request.Slot, state);
    }

    #endregion

    #region 站内任务（Job 任务表里的工艺走这里，转给上面的工艺入口）

    private static readonly IReadOnlyList<string> ChamberTasks = [StationTaskAction.Pick, StationTaskAction.Place, StationTaskAction.Process];

    /// <summary>
    /// 腔体支持取片、放片、工艺
    /// </summary>
    public override IReadOnlyList<string> SupportedTasks => ChamberTasks;

    /// <summary>
    /// 工艺转给 <see cref="CheckProcess"/>；别的站内任务腔体没有
    /// </summary>
    public override HandleResult CheckTask(StationTaskRequest request)
    {
        if (request.Kind != StationTaskAction.Process)
        {
            return base.CheckTask(request);
        }

        return CheckProcess(ToProcessRequest(request)) ?? HandleResult.Success();
    }

    /// <summary>
    /// 工艺转给 <see cref="StartProcess"/>：被拒回 null（调度下一拍再问）；别的站内任务腔体没有
    /// </summary>
    public override ModuleRoutine? StartTask(StationTaskRequest request)
    {
        if (request.Kind != StationTaskAction.Process)
        {
            return base.StartTask(request);
        }

        return StartProcess(ToProcessRequest(request)).Result;
    }

    /// <summary>
    /// Job 的站内任务换成起工艺的请求
    /// </summary>
    private static ProcessRequest ToProcessRequest(StationTaskRequest request)
    {
        return new ProcessRequest
        {
            Origin = ProcessOrigin.Job,
            Owner = request.Owner,
            WaferId = request.WaferId,
            Slot = request.Slot,
            Step = request.Step,
            RecipeName = request.RecipeName,
            Recipe = request.Recipe,
        };
    }

    #endregion

    /// <summary>
    /// 扫描周期
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        CheckDeviceAlarm(); //检查报警
        PublishState(); //推送状态
    }

    #region 报警

    /// <summary>
    /// 设备报警跟着设备报错走：有报错就报。报错没了也不清——报警只能人工 Reset 清
    /// </summary>
    private void CheckDeviceAlarm()
    {
        if (!string.IsNullOrEmpty(DeviceError))
        {
            RaiseAlarm(ChamberDeviceAlarm);
        }
    }

    /// <summary>
    /// 动作类报警：失败就报；动作成功也不清，只能人工 Reset 清。
    /// 人为急停顶掉的动作不报——那是操作员自己按的，不是故障。机型有自己的报警分法就重写（自己要管的情况先判、报了就 return，其余交给 base；整套换掉不调 base）；
    /// 在动作停下的回调里调、在模块锁里：只报警，别等待、别去拿别的模块的锁
    /// </summary>
    protected virtual void RaiseActionFailedAlarm(ModuleRoutine routine)
    {
        if (!routine.IsSuccess && routine.Code != ErrorCodes.Aborted)
        {
            RaiseAlarm(ControlledStopAlarm);
        }
    }

    #endregion
}
