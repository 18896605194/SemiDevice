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

public abstract class BaseChamberModule : BaseTransferStationModule
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
        @default: "60000", description: "部件手动动作超时（等门、Bowl、Lift、喷嘴、摆臂、旋转电机做完的上限）")]
    public int PartActionTimeout
    {
        get { return GetEcInt(nameof(PartActionTimeout)); }
        set { SetEcInt(nameof(PartActionTimeout), value); }
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

    protected override int AnchorState => ModuleState.Idle;

    #endregion

    protected BaseChamberModule()
    {
        RegisterTransitions(ChamberStateTable.ToModuleTable());
    }

    #region 启动前准备

    /// <summary>
    /// 启动前准备；由装配在 Start 之前调用。腔体这儿只占晶圆账的槽位，不连设备——
    /// 多个腔体通常挂在同一个 PLC 上，连接是那个 PLC 组件的事（它 Open 一次，腔体按地址读写），
    /// 摊到每个腔体里连就变成一台机器开 N 条连接了。
    /// 装机停用（IsEnable=False）的腔体连账都不占，空转。
    /// </summary>
    public override bool Open()
    {
        if (!IsEnable)
        {
            return true;
        }

        // 腔体在晶圆账里也是个位置：片停在腔里跟停在花篮里一样要有槽位。
        WaferManager.Current?.RegisterLocation(Name, SlotCount);
        return true;
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
        var manager = WaferManager.Current;
        if (manager is not null)
        {
            wafers = manager.GetSlots(Name);
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
    /// 模块状态和部件状态各推各的，都留存（供界面晚订阅或重连时补发）。
    /// </summary>
    protected override void PublishState()
    {
        var dto = CreateStateDto();
        if (dto.HasStateChanged(_lastPublishedState))
        {
            _lastPublishedState = dto;
            EventBus.Send(dto, Name);
        }

        PublishParts();
    }

    #endregion

    #region 部件（门、Bowl、旋转电机、摆臂及装在它上面的 Lift 和喷嘴）

    /// <summary>sc.xml 里腔门的节点名：门和 Bowl 都是气缸，只能按名字认。</summary>
    private const string DoorPartName = "Door";

    /// <summary>sc.xml 里 Bowl 的节点名。</summary>
    private const string BowlPartName = "Bowl";

    private volatile ChamberPartsDto? _lastPublishedParts;

    /// <summary>
    /// 部件状态快照（腔体手动页的三维图、部件按钮用）：按 sc.xml 的结构找部件——名叫 Door / Bowl 的气缸、
    /// 第一个旋转电机、每条摆臂（摆臂下面第一个气缸是它的 Lift，喷嘴按 sc 里的先后）。sc 里没配的部件不出现。
    /// </summary>
    public ChamberPartsDto CreatePartsDto()
    {
        var previous = _lastPublishedParts;
        var dto = new ChamberPartsDto
        {
            Name = Name,
            Door = CreateCylinderDto(FindChild<TwoStateComponent>(DoorPartName)),
            Bowl = CreateCylinderDto(FindChild<TwoStateComponent>(BowlPartName)),
            Spin = CreateSpinDto(FindChild<SpinMotorComponent>()),
        };

        foreach (var arm in FindChildren<ArmAxisComponent>())
        {
            ChamberArmDto? published = null;
            if (previous is not null)
            {
                published = previous.Arms.FirstOrDefault(item => item.Path == arm.FullPath);
            }

            dto.Arms.Add(new ChamberArmDto
            {
                Name = arm.Name,
                Path = arm.FullPath,
                Reach = ReachOf(arm, published),
                IsMoving = arm.IsBusy,
                Lift = CreateCylinderDto(arm.FindChild<TwoStateComponent>()),
                Nozzles = arm.FindChildren<NozzleComponent>()
                    .Select(nozzle => new ChamberNozzleDto
                    {
                        Name = nozzle.Name,
                        Path = nozzle.FullPath,
                        Chemical = nozzle.Chemical,
                        IsOn = nozzle.IsOn,
                    })
                    .ToList(),
            });
        }

        return dto;
    }

    private static ChamberCylinderDto? CreateCylinderDto(TwoStateComponent? cylinder)
    {
        if (cylinder is null)
        {
            return null;
        }

        return new ChamberCylinderDto
        {
            Name = cylinder.Name,
            Path = cylinder.FullPath,
            IsOpen = cylinder.IsOpenCommanded,
            IsMoving = cylinder.IsTraveling,
        };
    }

    private static ChamberSpinDto? CreateSpinDto(SpinMotorComponent? spin)
    {
        if (spin is null)
        {
            return null;
        }

        double speed = spin.CurrentSpeed;
        return new ChamberSpinDto
        {
            Name = spin.Name,
            Path = spin.FullPath,
            IsSpinning = spin.HasPlcData && Math.Abs(speed) > spin.SpeedTolerance,
            IsClockwise = speed >= 0,
        };
    }

    /// <summary>
    /// 摆臂摆到哪：0 = Home（回零后轴在 0 位），1 = 工艺位（EC Center），中间按轴位置线性换算，可以超出 0~1。
    /// Center 还没标定（跟 0 位分不开）时只分两档：在 0 位附近算 Home，离开了算工艺位。
    /// PLC 没数据时保持上次推的值；位置变化在到位容差以内不算动，免得编码器抖一下就推一次。
    /// </summary>
    private static double ReachOf(ArmAxisComponent arm, ChamberArmDto? published)
    {
        if (!arm.HasPlcData)
        {
            return published?.Reach ?? 0;
        }

        double position = arm.CurrentPosition;
        double center = arm.Center;
        double tolerance = arm.PositionTolerance;
        if (Math.Abs(center) <= tolerance)
        {
            return Math.Abs(position) <= tolerance ? 0 : 1;
        }

        double reach = position / center;
        if (published is not null && Math.Abs(reach - published.Reach) * Math.Abs(center) <= tolerance)
        {
            return published.Reach;
        }

        return reach;
    }

    /// <summary>部件状态有变化才推；token 是模块名，跟 ChamberDto 类型不同、互不覆盖。</summary>
    private void PublishParts()
    {
        var parts = CreatePartsDto();
        if (!parts.HasStateChanged(_lastPublishedParts))
        {
            return;
        }

        _lastPublishedParts = parts;
        EventBus.Send(parts, Name);
    }

    /// <summary>
    /// 按全路径（sc.xml 的组件路径，如 "Chamber1.Arm1.Lift"）找本腔体下的部件，忽略大小写；找不到返回 null。
    /// </summary>
    public ComponentBase? FindPart(string path)
    {
        foreach (var child in FindChildren<ComponentBase>())
        {
            if (string.Equals(child.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>部件支不支持这个动作：气缸开 / 关，喷嘴出液 / 停液，摆臂回零 / 去工艺位，旋转电机转 / 停。</summary>
    public static bool SupportsPartAction(ComponentBase part, ChamberPartAction action)
    {
        switch (part)
        {
            case TwoStateComponent:
                return action is ChamberPartAction.Open or ChamberPartAction.Close;

            case OneStateComponent:
                return action is ChamberPartAction.On or ChamberPartAction.Off;

            case SpinMotorComponent:
                return action is ChamberPartAction.Start or ChamberPartAction.Stop;

            case ArmAxisComponent:
                return action is ChamberPartAction.Home or ChamberPartAction.Center;

            default:
                return false;
        }
    }

    /// <summary>
    /// 发起部件手动动作：找部件 → 看支不支持 → 锁内确认状态允许、没有在途动作 → 发指令 → 挂上等部件做完的操作。
    /// 指令在这儿就发：发不出去（PLC 没连、轴没回零……）直接回 CommandRejected，模块状态不动、也不报警。
    /// 先确认能挂上再发，别的动作在途时发出去的指令就没人等了。执行中落 ChamberState.Manual，做完回原来的状态。
    /// 还没做联锁（比如 Bowl 升着不许摆臂），操作员自己看着点。
    /// </summary>
    public ChamberPartActionResult TryPartAction(string path, ChamberPartAction action, out ModuleOperation? operation)
    {
        operation = null;
        var part = FindPart(path);
        if (part is null)
        {
            return ChamberPartActionResult.NotFound;
        }

        if (!SupportsPartAction(part, action))
        {
            return ChamberPartActionResult.Unsupported;
        }

        lock (OperationGate)
        {
            var current = CurrentOperation;
            bool busy = current is not null && !current.IsTerminal;
            if (!CanBeginAction || busy || !TryGetTransition(State, nameof(ChamberAction.Manual), out _))
            {
                return ChamberPartActionResult.Rejected;
            }

            if (!SendPartCommand(part, action))
            {
                return ChamberPartActionResult.CommandRejected;
            }

            operation = Begin(ChamberAction.Manual, new ChamberPartOperation(part, action, PartActionTimeout));
            if (operation is null)
            {
                return ChamberPartActionResult.Rejected;
            }

            return ChamberPartActionResult.Started;
        }
    }

    /// <summary>发部件指令；返回 true 只表示指令已经写进 PLC，做没做完由 ChamberPartOperation 看部件的动作状态。</summary>
    private static bool SendPartCommand(ComponentBase part, ChamberPartAction action)
    {
        switch (part)
        {
            case TwoStateComponent cylinder:
                return action == ChamberPartAction.Open ? cylinder.Open() : cylinder.Close();

            case OneStateComponent valve:
                return action == ChamberPartAction.On ? valve.On() : valve.Off();

            case SpinMotorComponent spin:
                return action == ChamberPartAction.Start ? spin.Spin(spin.ManualSpeed) : spin.Stop();

            case ArmAxisComponent arm:
                return action == ChamberPartAction.Home ? arm.Home() : arm.MoveTo(arm.Center);

            default:
                return false;
        }
    }

    #endregion

    #region Action（动作体由机型实现——直接创建操作）

    /// <summary>
    /// 装机停用的腔体不发动作（腔体不持驱动，能不能发只看这一条）。
    /// </summary>
    protected override bool CanBeginAction => IsEnable;

    /// <summary>
    /// 发起回原点。机型实现：Begin(ChamberAction.Home, new ...Operation(...))。
    /// </summary>
    public abstract ModuleOperation? Home();

    /// <summary>
    /// 初始化（重写组件基类的 Init）：先初始化子组件，再回原点——Home 就是腔体的初始化。
    /// 返回 Home 操作，调用方等它做完；状态不允许时为 null。
    /// </summary>
    public override ModuleOperation? Init()
    {
        base.Init();
        return Home();
    }

    /// <summary>
    /// 复位（重写组件基类的 Reset）：先清报警、复位子组件，再发设备复位清错。
    /// 卡在交互环里（取放片失败停在 Transferring）时也能发，落 Idle 等于强制脱离这一轮交互。
    /// </summary>
    public override ModuleOperation? Reset()
    {
        base.Reset();
        return ResetDevice();
    }

    /// <summary>
    /// 发设备复位清错。机型实现：Begin(ChamberAction.Reset, new ...Operation(...))。
    /// </summary>
    protected abstract ModuleOperation? ResetDevice();

    /// <summary>
    /// 中止（重写组件基类的 Abort，急停）：先中止子组件，再发设备中止；Abort 可顶替在途动作，不清报警。
    /// </summary>
    public override ModuleOperation? Abort()
    {
        base.Abort();
        return AbortDevice();
    }

    /// <summary>
    /// 发设备中止。机型实现：Begin(ChamberAction.Abort, new ...Operation(...))。
    /// </summary>
    protected abstract ModuleOperation? AbortDevice();

    /// <summary>
    /// 起工艺。只在 Idle（门关、机械手不在里面）时允许；腔里有没有片由调用方查晶圆账。
    /// 配方怎么传、传什么，等工艺定下来再收窄——现在先按名字给。发起成功记下配方名（SV Recipe），界面据此显示当前配方。
    /// 返回工艺操作，调用方等它做完；状态不允许时为 null。
    /// </summary>
    public ModuleOperation? Process(string recipe)
    {
        var operation = StartProcess(recipe);
        if (operation is not null)
        {
            Recipe = recipe;
        }

        return operation;
    }

    /// <summary>
    /// 发起工艺。机型实现：Begin(ChamberAction.Process, new ...Operation(...))。
    /// </summary>
    protected abstract ModuleOperation? StartProcess(string recipe);

    /// <summary>
    /// 操作终结（状态已由基类落好）：失败的动作报警。
    /// </summary>
    protected override void OnOperationCompleted(ModuleOperation operation)
    {
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
}
