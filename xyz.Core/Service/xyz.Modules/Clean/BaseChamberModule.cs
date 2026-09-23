using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Modules.Enums;
using xyz.Modules.StateMachines;
using xyz.Shared.Errors;

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

    #endregion

    #region SC

    [SCEditor("", "Chamber", "腔体品牌")]
    public string Brand { get; set; } = string.Empty;

    [SCEditor("True", "Chamber", "是否启用本腔体 (False=装机未接/停用)")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("1", "Chamber", "片位数（腔体一般 1 片；晶圆账按它注册槽位）")]
    public int SlotCount { get; set; } = 1;

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
    /// 配方怎么传、传什么，等工艺定下来再收窄——现在先按名字给。
    /// 机型实现：Begin(ChamberAction.Process, new ...Operation(...))。
    /// </summary>
    public abstract ModuleOperation? Process(string recipe);

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
