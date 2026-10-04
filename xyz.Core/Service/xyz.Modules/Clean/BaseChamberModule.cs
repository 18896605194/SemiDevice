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
        @default: "60000", description: "部件手动动作超时（等轴、气缸这些部件做完的上限；点动是松手后等停下的上限）")]
    public int PartActionTimeout
    {
        get { return GetEcInt(nameof(PartActionTimeout)); }
        set { SetEcInt(nameof(PartActionTimeout), value); }
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

    protected override int AnchorState => ModuleState.Idle;

    #endregion

    protected BaseChamberModule()
    {
        RegisterTransitions(ChamberStateTable.ToModuleTable());
        _parts = new Lazy<PartCatalog>(() => new PartCatalog(this));
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

    #region 部件（sc.xml 里标了 [PartKind] 的组件：轴、气缸、阀……）

    /// <summary>手动部件表：第一次用时按组件树建（装配时子组件是构造之后才挂上的，不能在构造里建）。</summary>
    private readonly Lazy<PartCatalog> _parts;

    private volatile ModulePartsDto? _lastPublishedParts;

    /// <summary>
    /// 部件快照（腔体手动页的轴页签、气缸表、三维图用）：sc.xml 里标了种类的组件和它们标了 [LiveValue] 的数据，按 sc 的先后。
    /// 部件、数据都是组件自己声明的，这里不认具体是什么硬件。
    /// </summary>
    public ModulePartsDto CreatePartsDto()
    {
        return _parts.Value.CreateDto();
    }

    /// <summary>部件数据有变化才推；token 是模块名，跟 ChamberDto 类型不同、互不覆盖。</summary>
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
    /// 发起部件手动动作：找部件 → 找动作（组件上标了 [ManualAction] 的同名方法）→ 转参数 → 按动作类别发：
    /// <list type="bullet">
    /// <item>停止类（如轴停止）：不看腔体忙不忙、不挂操作，发出去就回 Sent；正按住的点动算松手。</item>
    /// <item>普通动作：锁内确认状态允许、没有在途动作 → 发指令 → 挂上等部件做完的操作，回 Started。</item>
    /// <item>按住类（如点动）：同普通动作，挂的是等松手的操作，回 Holding；按住期间界面调 RenewPartAction 续。</item>
    /// </list>
    /// 指令在这儿就发：发不出去（PLC 没连、轴没回零……）直接回 CommandRejected，模块状态不动、也不报警。
    /// 先确认能挂上再发，别的动作在途时发出去的指令就没人等了。执行中落 ChamberState.Manual，做完回原来的状态。
    /// 还没做联锁（比如 Bowl 升着不许摆臂），操作员自己看着点。
    /// </summary>
    public ChamberPartActionResult TryPartAction(string path, string action, IReadOnlyList<string> args, out ModuleOperation? operation)
    {
        operation = null;
        var part = _parts.Value.Find(path);
        if (part is null)
        {
            return ChamberPartActionResult.NotFound;
        }

        if (!part.TryGetAction(action, out var entry))
        {
            return ChamberPartActionResult.Unsupported;
        }

        if (!entry.TryBind(args, out var values))
        {
            return ChamberPartActionResult.InvalidArgs;
        }

        if (entry.IsPriority)
        {
            if (!entry.Invoke(part.Component, values))
            {
                return ChamberPartActionResult.CommandRejected;
            }

            if (CurrentOperation is ChamberHoldOperation hold && hold.Part == part.Component)
            {
                hold.Release();
            }

            return ChamberPartActionResult.Sent;
        }

        // 按住类先确认松手动作在，免得点动发出去了却停不下来。
        ManualPartAction? release = null;
        if (entry.Release is not null && (!part.TryGetAction(entry.Release, out release) || !release.TryBind([], out _)))
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

            if (!entry.Invoke(part.Component, values))
            {
                return ChamberPartActionResult.CommandRejected;
            }

            if (release is null)
            {
                operation = Begin(ChamberAction.Manual, new ChamberPartOperation(part.Component, entry.Name, PartActionTimeout));
                return operation is null ? ChamberPartActionResult.Rejected : ChamberPartActionResult.Started;
            }

            var component = part.Component;
            ManualPartAction releaseAction = release;
            Func<bool> stop = () => releaseAction.Invoke(component, []);
            operation = Begin(ChamberAction.Manual,
                new ChamberHoldOperation(component, entry.Name, stop, HoldTimeoutMs, PartActionTimeout));
            if (operation is null)
            {
                // 状态在锁里查过，挂不上只是防万一；点动已经发出去了，先停下来。
                stop();
                return ChamberPartActionResult.Rejected;
            }

            return ChamberPartActionResult.Holding;
        }
    }

    /// <summary>
    /// 续按住类动作：正在按住的就是这个部件的这个动作才续上（重新计时），否则返回 false——已经松手、被停止或中止顶掉了。
    /// </summary>
    public bool RenewPartAction(string path, string action)
    {
        var part = _parts.Value.Find(path);
        if (part is null)
        {
            return false;
        }

        return CurrentOperation is ChamberHoldOperation hold
            && hold.Part == part.Component
            && string.Equals(hold.Action, action, StringComparison.OrdinalIgnoreCase)
            && hold.Renew();
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
