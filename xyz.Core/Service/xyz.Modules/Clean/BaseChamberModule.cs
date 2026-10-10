using System.Globalization;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// 腔体模块基类
/// </summary>
public abstract class BaseChamberModule : BaseTransferStationModule, IProcessStation
{
    #region SV

    /// <summary>
    /// 腔体当前状态（SV）。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Int, description: "模块状态码")]
    public override int State { get; protected set; } = ModuleState.NotInit;

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

    private volatile string? _recipe;

    /// <summary>
    /// 当前配方（SV）：最近一次发起成功的工艺配方名；还没做过工艺为 null。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "当前工艺配方名")]
    public string? Recipe
    {
        get => _recipe;
        private set => _recipe = value;
    }

    #endregion

    #region SC

    [SCEditor("", "Chamber", "腔体品牌")]
    public string Brand { get; set; } = string.Empty;

    [SCEditor("True", "Chamber", "是否启用本腔体 (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("1", "Chamber", "片位数（腔体一般 1 片；晶圆账按它注册槽位）")]
    public override int SlotCount { get; set; } = 1;

    // 通讯参数（IP/端口/串口号）不在基类：腔体走 PLC 还是串口网口由机型定，
    // 机型自己声明自己的 [SCEditor]，别在这儿预设一套用不上的。

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
        @default: "60000", description: "Home 动作超时（回原点）")]
    public int HomeTimeout
    {
        get { return GetEcInt(nameof(HomeTimeout)); }
        set { SetEcInt(nameof(HomeTimeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "3600000",
        @default: "600000", description: "工艺超时（一支配方跑完的上限）")]
    public int ProcessTimeout
    {
        get { return GetEcInt(nameof(ProcessTimeout)); }
        set { SetEcInt(nameof(ProcessTimeout), value); }
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

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "500", max: "10000",
        @default: "1000", description: "点动保活超时：手动页按住点动时界面每 200 ms 续一次，超过这么久没续上（界面断了、客户端退了）就自己停")]
    public int HoldTimeoutMs
    {
        get { return GetEcInt(nameof(HoldTimeoutMs)); }
        set { SetEcInt(nameof(HoldTimeoutMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("腔体设备报警", AlarmCategory.HardwareError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "腔体控制器上报报错",
        Solution = "查询报错内容，排除故障后复位并重新回原点")]
    public string ChamberDeviceAlarm = nameof(ChamberDeviceAlarm);

    [Alarm("腔体受控停止", AlarmCategory.ProcessError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "回原点、工艺等动作失败或超时，腔体进入错误状态",
        Solution = "先确认腔内实际片位与工艺是否跑完，再复位并重新回原点")]
    public string ControlledStopAlarm = nameof(ControlledStopAlarm);

    #endregion

    #region 站点环

    protected override int StandbyState => ChamberState.Idle;

    #endregion

    protected BaseChamberModule()
    {
        RegisterTransitions(ChamberStateTable.ToModuleTable());
    }

    #region 组件初始化

    private WaferManagerComponent? _waferManager;

    public override bool InitComponent()
    {
        if (!IsEnable)
        {
            return true;
        }

        // 腔体在晶圆账里也是个位置：片停在腔里跟停在花篮里一样要有槽位。
        _waferManager = WaferManagerComponent.Current;
        _waferManager?.RegisterLocation(Name, SlotCount);
        return base.InitComponent();
    }

    #endregion

    #region 状态发布

    private ChamberDto? _lastPublishedState;

    /// <summary>
    /// 当前状态快照，状态发布与 GetState 查询共用。
    /// 停用的腔体不连设备，设备报错不可信，置 null；片位按晶圆账逐位取（片号 + 工艺状态）。
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
    /// 片位表：按 SlotCount 逐位查晶圆账；账上没注册这个腔体（停用、或 sc.xml 没配晶圆账）时全给空。
    /// </summary>
    private List<ChamberSlotDto> CreateSlotDtos()
    {
        IReadOnlyList<WaferInfo?> wafers = [];
        if (_waferManager is not null)
        {
            wafers = _waferManager.GetSlots(Name);
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
    /// 账上的片转片位状态：没片为空，有片按它的工艺状态。
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
    /// 发布当前状态（机型扫描周期调用，状态环改完状态也会立即调）：首次发布，之后只在状态变化时发布。
    /// 模块状态和设备状态各推各的，都留存（供界面晚订阅或重连时补发）。
    /// </summary>
    protected override void PublishState()
    {
        var dto = CreateStateDto();
        if (dto.HasStateChanged(_lastPublishedState))
        {
            _lastPublishedState = dto;
            EventBus.Send(dto, Name);
        }

        PublishDeviceData();
    }

    #endregion

    #region 腔体里装的（sc.xml 这个腔体节点下面挂的）

    /// <summary>腔门的节点名：门和 Bowl 都是气缸，只能按名字认。</summary>
    private const string DoorName = "Door";

    /// <summary>Bowl 节点名的开头（Bowl1、Bowl2……）。</summary>
    private const string BowlPrefix = "Bowl";

    /// <summary>腔门：腔体下名叫 Door 的气缸；sc 里没配为 null。</summary>
    public TwoStateComponent? Door
    {
        get
        {
            return Children.OfType<TwoStateComponent>()
                .FirstOrDefault(cylinder => string.Equals(cylinder.Name, DoorName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Bowl：腔体下名字以 Bowl 开头的气缸（可以几层），按 sc 的先后。</summary>
    public IReadOnlyList<TwoStateComponent> Bowls
    {
        get
        {
            return Children.OfType<TwoStateComponent>()
                .Where(cylinder => cylinder.Name.StartsWith(BowlPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>卡盘：第一个旋转电机；sc 里没配为 null。</summary>
    public SpinMotorComponent? SpinMotor => FindChild<SpinMotorComponent>();

    /// <summary>摆臂（它上面的 Lift、喷嘴在摆臂组件上），按 sc 的先后。</summary>
    public IReadOnlyList<SwingArmComponent> Arms => FindChildren<SwingArmComponent>();

    /// <summary>所有轴（卡盘、摆臂……）。</summary>
    public IReadOnlyList<AxisComponent> Axes => FindChildren<AxisComponent>();

    /// <summary>所有双作用气缸（门、Bowl、Lift……）。</summary>
    public IReadOnlyList<TwoStateComponent> Cylinders => FindChildren<TwoStateComponent>();

    /// <summary>所有喷嘴。</summary>
    public IReadOnlyList<NozzleComponent> Nozzles => FindChildren<NozzleComponent>();

    /// <summary>按节点名找摆臂（配方里选的就是节点名，如 "Arm1"，忽略大小写）；没有为 null。</summary>
    public SwingArmComponent? FindArm(string name)
    {
        return Arms.FirstOrDefault(arm => string.Equals(arm.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按全路径找轴（手动页按推送里的 Path 发，忽略大小写）；没有为 null。</summary>
    private AxisComponent? FindAxis(string path)
    {
        return Axes.FirstOrDefault(axis => string.Equals(axis.FullPath, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按全路径找气缸；没有为 null。</summary>
    private TwoStateComponent? FindCylinder(string path)
    {
        return Cylinders.FirstOrDefault(cylinder => string.Equals(cylinder.FullPath, path, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region 设备状态推送（手动页的轴页签、气缸表、三维图）

    /// <summary>位置、速度、摆幅推几位小数：编码器在这一位以下抖不会一直推。</summary>
    private const int PushDecimals = 3;

    private volatile ChamberDeviceDataDto? _lastPublishedDeviceData;

    /// <summary>
    /// 设备状态快照，结构跟 sc 一样：门、Bowl、卡盘、各条摆臂（摆臂下面是它的 Lift 和喷嘴）。
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
            var spinDto = AxisDtoOf<ChamberSpinDto>(spin);
            spinDto.IsSpinning = spin.IsSpinning;
            dto.Spin = spinDto;
        }

        foreach (var arm in Arms)
        {
            var armDto = AxisDtoOf<ChamberArmDto>(arm);
            armDto.Reach = RoundForPush(arm.Reach);
            armDto.EdgeReach = RoundForPush(arm.EdgeReach);
            var lift = arm.Lift;
            armDto.Lift = lift is null ? null : CylinderDtoOf(lift);
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

    /// <summary>设备状态有变化才推；token 是模块名，跟 ChamberDto 类型不同、互不覆盖。</summary>
    private void PublishDeviceData()
    {
        var data = CreateDeviceDataDto();
        if (!data.HasStateChanged(_lastPublishedDeviceData))
        {
            return;
        }

        _lastPublishedDeviceData = data;
        EventBus.Send(data, Name);
    }

    /// <summary>一根轴的位置、速度、五盏灯（卡盘、摆臂共用这一段，各自的再另外填）。</summary>
    private static T AxisDtoOf<T>(AxisComponent axis) where T : ChamberAxisDto, new()
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

    private static ChamberCylinderDto CylinderDtoOf(TwoStateComponent cylinder)
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

    /// <summary>按推的位数四舍五入（-0 记成 0）；不是有限数记 0。</summary>
    private static double RoundForPush(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        double rounded = Math.Round(value, PushDecimals, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }

    #endregion

    #region 设备手动动作（手动页：轴回零 / 移动 / 步进 / 点动 / 停止 / 复位，气缸升 / 降）

    // 指令在这儿就发：发不出去（PLC 没连、轴没回零……）直接回 CommandRejected，模块状态不动、也不报警。
    // 先确认能挂上再发，别的动作在途时发出去的指令就没人等了。执行中落 ChamberState.Manual，做完回原来的状态。
    // 还没做联锁（比如 Bowl 升着不许摆臂），操作员自己看着点。

    /// <summary>轴回零。</summary>
    public ChamberDeviceActionResult AxisHome(string path, out ModuleOperation? operation)
    {
        operation = null;
        var axis = FindAxis(path);
        if (axis is null)
        {
            return ChamberDeviceActionResult.NotFound;
        }

        return StartDeviceAction(axis, ChamberDeviceAction.Home, axis.Home, out operation);
    }

    /// <summary>轴走到绝对位置；speed 为 0 按这根轴的 EC MoveSpeed。</summary>
    public ChamberDeviceActionResult AxisMove(string path, double position, double speed, out ModuleOperation? operation)
    {
        operation = null;
        var axis = FindAxis(path);
        if (axis is null)
        {
            return ChamberDeviceActionResult.NotFound;
        }

        if (!double.IsFinite(position) || !IsSpeed(speed))
        {
            return ChamberDeviceActionResult.InvalidArgs;
        }

        return StartDeviceAction(axis, ChamberDeviceAction.Move, () => axis.MoveTo(position, SpeedOrDefault(speed)), out operation);
    }

    /// <summary>轴走一段（正负是方向，不能是 0）；speed 为 0 按这根轴的 EC MoveSpeed。</summary>
    public ChamberDeviceActionResult AxisStep(string path, double distance, double speed, out ModuleOperation? operation)
    {
        operation = null;
        var axis = FindAxis(path);
        if (axis is null)
        {
            return ChamberDeviceActionResult.NotFound;
        }

        if (!double.IsFinite(distance) || distance == 0 || !IsSpeed(speed))
        {
            return ChamberDeviceActionResult.InvalidArgs;
        }

        return StartDeviceAction(axis, ChamberDeviceAction.Step, () => axis.MoveBy(distance, SpeedOrDefault(speed)), out operation);
    }

    /// <summary>轴复位清错。</summary>
    public ChamberDeviceActionResult AxisReset(string path, out ModuleOperation? operation)
    {
        operation = null;
        var axis = FindAxis(path);
        if (axis is null)
        {
            return ChamberDeviceActionResult.NotFound;
        }

        return StartDeviceAction(axis, ChamberDeviceAction.Reset, axis.ResetDrive, out operation);
    }

    /// <summary>气缸升（true，开侧）或降。</summary>
    public ChamberDeviceActionResult MoveCylinder(string path, bool up, out ModuleOperation? operation)
    {
        operation = null;
        var cylinder = FindCylinder(path);
        if (cylinder is null)
        {
            return ChamberDeviceActionResult.NotFound;
        }

        return up
            ? StartDeviceAction(cylinder, ChamberDeviceAction.Up, cylinder.Open, out operation)
            : StartDeviceAction(cylinder, ChamberDeviceAction.Down, cylinder.Close, out operation);
    }

    /// <summary>
    /// 轴停止：不看腔体忙不忙、不挂操作，发出去就回 Sent；正按着的点动算松手（之后等轴停下就退出"手动中"）。
    /// </summary>
    public ChamberDeviceActionResult AxisStop(string path)
    {
        var axis = FindAxis(path);
        if (axis is null)
        {
            return ChamberDeviceActionResult.NotFound;
        }

        if (!axis.Stop())
        {
            return ChamberDeviceActionResult.CommandRejected;
        }

        if (CurrentOperation is ChamberHoldOperation hold && hold.Axis == axis)
        {
            hold.Release();
        }

        return ChamberDeviceActionResult.Sent;
    }

    /// <summary>
    /// 轴点动（速度正负是方向，不能是 0）：发起就回 Holding；按住期间界面调 <see cref="AxisJogRenew"/> 续，
    /// 松手发 <see cref="AxisStop"/>；EC HoldTimeoutMs 内没续上模块自己停。
    /// </summary>
    public ChamberDeviceActionResult AxisJog(string path, double speed)
    {
        var axis = FindAxis(path);
        if (axis is null)
        {
            return ChamberDeviceActionResult.NotFound;
        }

        if (!double.IsFinite(speed) || speed == 0)
        {
            return ChamberDeviceActionResult.InvalidArgs;
        }

        lock (OperationGate)
        {
            if (!CanStartDeviceAction())
            {
                return ChamberDeviceActionResult.Rejected;
            }

            if (!axis.Jog(speed))
            {
                return ChamberDeviceActionResult.CommandRejected;
            }

            if (Begin(ChamberAction.Manual, new ChamberHoldOperation(axis, HoldTimeoutMs, DeviceActionTimeout)) is null)
            {
                // 状态在锁里查过，挂不上只是防万一；点动已经发出去了，先停下来。
                axis.Stop();
                return ChamberDeviceActionResult.Rejected;
            }

            return ChamberDeviceActionResult.Holding;
        }
    }

    /// <summary>续点动：正在点动的就是这根轴才续上（重新计时），否则返回 false——已经松手、被停止或中止顶掉了。</summary>
    public bool AxisJogRenew(string path)
    {
        var axis = FindAxis(path);
        return axis is not null
            && CurrentOperation is ChamberHoldOperation hold
            && hold.Axis == axis
            && hold.Renew();
    }

    /// <summary>锁内确认状态允许、没有在途动作 → 发指令 → 挂上等设备做完的操作，回 Started。</summary>
    private ChamberDeviceActionResult StartDeviceAction(ComponentBase part, ChamberDeviceAction action, Func<bool> send, out ModuleOperation? operation)
    {
        operation = null;
        lock (OperationGate)
        {
            if (!CanStartDeviceAction())
            {
                return ChamberDeviceActionResult.Rejected;
            }

            if (!send())
            {
                return ChamberDeviceActionResult.CommandRejected;
            }

            operation = Begin(ChamberAction.Manual, new ChamberDeviceOperation(part, action, DeviceActionTimeout));
            return operation is null ? ChamberDeviceActionResult.Rejected : ChamberDeviceActionResult.Started;
        }
    }

    /// <summary>能不能起设备动作（在模块锁里调）：腔体启用、没有在途动作、当前状态允许手动。</summary>
    private bool CanStartDeviceAction()
    {
        var current = CurrentOperation;
        bool busy = current is not null && !current.IsTerminal;
        return CanBeginAction && !busy && TryGetTransition(State, nameof(ChamberAction.Manual), out _);
    }

    /// <summary>速度：0 = 用 EC 默认值，不能是负的、不能不是有限数。</summary>
    private static bool IsSpeed(double speed)
    {
        return double.IsFinite(speed) && speed >= 0;
    }

    private static double? SpeedOrDefault(double speed)
    {
        return speed > 0 ? speed : null;
    }

    #endregion

    #region Action（平台默认做法：照 sc 里挂的设备发；机型不一样就重写）

    /// <summary>
    /// 装机停用的腔体不发动作（腔体不持驱动，能不能发只看这一条）。
    /// </summary>
    protected override bool CanBeginAction => IsEnable;

    /// <summary>
    /// 回原点：喷嘴全关 → 卡盘停转 → Lift 升 → 摆臂（和别的轴）回零 → Bowl 降，门不动（<see cref="ChamberHomeOperation"/>）。
    /// 机型的先后不一样就重写：Begin(ChamberAction.Home, new ...Operation(...))。
    /// </summary>
    public virtual ModuleOperation? Home()
    {
        return Begin(ChamberAction.Home, new ChamberHomeOperation(this, HomeTimeout));
    }

    /// <summary>
    /// 模块初始化（动硬件，重写 BaseModule 的 InitModule）：回原点——Home 就是腔体的初始化。
    /// 人或调度才调，开机不调。返回 Home 操作，调用方等它做完；状态不允许时为 null。
    /// </summary>
    public override ModuleOperation? InitModule()
    {
        return Home();
    }

    /// <summary>
    /// 复位（重写组件基类的 Reset）：先清报警、复位子组件（轴在这一步发驱动器复位），再等设备复位做完。
    /// 卡在交互环里（取放片失败停在 Transferring）时也能发，落 Idle 等于强制脱离这一轮交互。
    /// </summary>
    public override ModuleOperation? Reset()
    {
        base.Reset();
        return ResetDevice();
    }

    /// <summary>
    /// 设备复位：等每根轴把驱动器复位做完（<see cref="ChamberResetOperation"/>）。机型不一样就重写：Begin(ChamberAction.Reset, ...)。
    /// </summary>
    protected virtual ModuleOperation? ResetDevice()
    {
        return Begin(ChamberAction.Reset, new ChamberResetOperation(this, ResetTimeout));
    }

    /// <summary>
    /// 中止（重写组件基类的 Abort，急停）：先中止子组件（轴在这一步发停止），再做设备中止；Abort 可顶替在途动作，不清报警。
    /// </summary>
    public override ModuleOperation? Abort()
    {
        base.Abort();
        return AbortDevice();
    }

    /// <summary>
    /// 设备中止：喷嘴全部停液、等所有轴停下（<see cref="ChamberAbortOperation"/>）。机型不一样就重写：Begin(ChamberAction.Abort, ...)。
    /// </summary>
    protected virtual ModuleOperation? AbortDevice()
    {
        return Begin(ChamberAction.Abort, new ChamberAbortOperation(this, AbortTimeout));
    }

    /// <summary>
    /// 做出一次工艺的操作：按请求里的配方快照一步一步转、摆、喷（<see cref="ChamberProcessOperation"/>，上限 EC ProcessTimeout）。
    /// 只管造操作，发不发得出去（状态、片、配方）基类已经查过、Begin 也由基类做。机型做法不一样就重写。
    /// </summary>
    protected virtual ModuleOperation? CreateProcessOperation(ProcessRequest request)
    {
        return new ChamberProcessOperation(this, request, ProcessTimeout);
    }

    /// <summary>
    /// 操作终结（状态已由基类落好）：失败的动作报警；工艺做完把账上的片标成完成 / 失败 / 中止。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
        RaiseActionFailedAlarm(operation);
        FinishProcess(operation);
    }

    #endregion

    #region 加工（IProcessStation：Job 和手动起工艺走同一个口子）

    private volatile ProcessRequest? _process;

    /// <summary>正在跑的工艺操作；只在模块锁里读写。</summary>
    private ModuleOperation? _processOperation;

    /// <summary>正在跑的工艺请求；没在跑为 null。</summary>
    public ProcessRequest? CurrentProcess => _process;

    /// <summary>
    /// 现在能不能起这个工艺（不动设备）：先查请求本身（配方名、配方对不对得上这个腔体、腔里是不是要做的那一片），
    /// 再查腔体此刻的状态——配方不对这种问题不管腔体忙不忙都先报出来。
    /// </summary>
    public ProcessRejection? CheckProcess(ProcessRequest request)
    {
        string slot = request.Slot.ToString(CultureInfo.InvariantCulture);
        if (request.RecipeName.Trim().Length == 0)
        {
            return new ProcessRejection(ErrorCodes.RecipeRequired, [Name]);
        }

        // 配方里下拉选的（摆臂、药液这类从腔体设备取的）这个腔体也得有：几个腔体装的不一样时，别的腔体的配方起不了
        var recipe = request.Recipe;
        var library = ProcessRecipeComponent.Current;
        if (recipe is not null && library is not null)
        {
            var mismatch = library.FindMismatch(recipe, Name);
            if (mismatch is not null)
            {
                return new ProcessRejection(ErrorCodes.ChamberRecipeOptionMissing, [Name, request.RecipeName.Trim(), mismatch.Field, mismatch.Value]);
            }
        }

        if (request.Slot < 1 || request.Slot > SlotCount)
        {
            return new ProcessRejection(ErrorCodes.ChamberWaferMismatch, [Name, slot]);
        }

        // 指定了片的（Job 起的）：腔里得正好是那一片，换过片、片没到都不起
        var expected = request.WaferId;
        if (expected is not null)
        {
            var wafer = _waferManager?.Get(Name, request.Slot);
            if (wafer is null || wafer.Id != expected.Value)
            {
                return new ProcessRejection(ErrorCodes.ChamberWaferMismatch, [Name, slot]);
            }
        }

        lock (OperationGate)
        {
            var current = CurrentOperation;
            bool busy = current is not null && !current.IsTerminal;
            if (!CanBeginAction || busy || !TryGetTransition(State, nameof(ChamberAction.Process), out _))
            {
                return new ProcessRejection(ErrorCodes.ActionRejected, [Name, State.ToString(CultureInfo.InvariantCulture)]);
            }
        }

        return null;
    }

    /// <summary>
    /// 起工艺：查过了才发。发起和"账上标加工中"在同一把锁里——扫描线程拿不到锁就推进不了这一步，
    /// 不会出现工艺已经做完、账才被标成加工中的倒挂。发起成功记下配方名（SV Recipe），界面据此显示当前配方。
    /// </summary>
    public ModuleOperation? StartProcess(ProcessRequest request)
    {
        if (CheckProcess(request) is not null)
        {
            return null;
        }

        lock (OperationGate)
        {
            var built = CreateProcessOperation(request);
            if (built is null)
            {
                return null;
            }

            var operation = Begin(ChamberAction.Process, built);
            if (operation is null)
            {
                return null;
            }

            _process = request;
            _processOperation = operation;
            Recipe = request.RecipeName.Trim();

            if (_waferManager is not null && _waferManager.HasWafer(Name, request.Slot))
            {
                _waferManager.SetProcessState(Name, request.Slot, WaferProcessState.InProcess);
            }

            return operation;
        }
    }

    /// <summary>
    /// 工艺操作终结（在模块锁里）：账上的片按结果标完成 / 失败 / 中止；不是工艺操作不管。
    /// </summary>
    private void FinishProcess(ModuleOperation operation)
    {
        if (!ReferenceEquals(operation, _processOperation))
        {
            return;
        }

        var request = _process;
        _process = null;
        _processOperation = null;

        // 工艺做到一半失败、超时：先把喷嘴停了，别一直喷（中止由中止操作自己停液）
        if (!operation.IsSuccess && operation.State != OperationState.Aborted)
        {
            foreach (var nozzle in Nozzles)
            {
                nozzle.Stop();
            }
        }

        if (request is null)
        {
            return;
        }

        if (_waferManager is null || !_waferManager.HasWafer(Name, request.Slot))
        {
            return;
        }

        var state = operation.IsSuccess
            ? WaferProcessState.Completed
            : operation.State == OperationState.Aborted ? WaferProcessState.Aborted : WaferProcessState.Failed;
        _waferManager.SetProcessState(Name, request.Slot, state);
    }

    #endregion

    #region 站内任务（Job 任务表里的工艺走这里，转给上面的加工口）

    private static readonly IReadOnlyList<string> ChamberTasks = [StationTaskAction.Pick, StationTaskAction.Place, StationTaskAction.Process];

    /// <summary>腔体支持取片、放片、工艺。</summary>
    public override IReadOnlyList<string> SupportedTasks => ChamberTasks;

    /// <summary>工艺转给 <see cref="CheckProcess"/>；别的站内任务腔体没有。</summary>
    public override HandleResult CheckTask(StationTaskRequest request)
    {
        if (request.Kind != StationTaskAction.Process)
        {
            return base.CheckTask(request);
        }

        var rejection = CheckProcess(ToProcessRequest(request));
        return rejection is null ? HandleResult.Success() : HandleResult.Fail(rejection.Code, [.. rejection.Args]);
    }

    /// <summary>工艺转给 <see cref="StartProcess"/>。</summary>
    public override ModuleOperation? StartTask(StationTaskRequest request)
    {
        return request.Kind == StationTaskAction.Process ? StartProcess(ToProcessRequest(request)) : base.StartTask(request);
    }

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

    #region 报警

    /// <summary>
    /// 扫描周期：先扫子组件与操作（基类），再按设备报错刷新报警，最后发布状态（有变化才发）。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        CheckDeviceAlarm();
        PublishState();
    }

    /// <summary>
    /// 设备报警跟着设备报错走：有报错就报。报错没了也不清——报警只能人工 Reset 清。
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
}
