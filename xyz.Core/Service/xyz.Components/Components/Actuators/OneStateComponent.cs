using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// 单 DO 执行器底座（阀、喷嘴）：一个 DO 开关，到位反馈挂子节点 Feedback（DiSensorComponent），没挂就不等反馈
/// </summary>
public abstract class OneStateComponent : ComponentBase
{
    #region  SC

    [SCEditor("-1", "IO", "驱动 DO 索引", Required = true)]
    public int DoIndex { get; set; } = -1;

    [SCEditor("NC", "IO", "常开常闭：NC 常闭（DO 写 true 开）、NO 常开（DO 写 false 开）")]
    public NormalState NormalState { get; set; } = NormalState.NC;

    #endregion

    /// <summary>
    /// 到位反馈（子节点 Feedback）；没挂是 null，开了就算完成
    /// </summary>
    private IDiSensor? _feedback;

    public override bool InitComponent()
    {
        _feedback = FindSignal("Feedback");
        return base.InitComponent();
    }

    /// <summary>
    /// IO 组件（开机 Bind 时取）；sc.xml 没配 Io 节点是 null
    /// </summary>
    protected IIoComponent? Io { get; private set; }

    public override void Bind()
    {
        Io = IocHelper.GetService<IIoComponent>();
    }

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "3000",
        "到位超时时间（没挂 Feedback 时不生效）")]
    public int ActionTimeoutMs
    {
        get { return GetEcInt(nameof(ActionTimeoutMs)); }
        set { SetEcInt(nameof(ActionTimeoutMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("到位反馈超时", AlarmCategory.Timeout,
        Description = "驱动命令已发出，但在指定时间内未收到到位反馈",
        Solution = "检查气源压力、电磁阀及到位传感器；没接到位点就不挂 Feedback 子节点，"
                   + "不等也不报；超时时长在 EC ActionTimeoutMs 调")]
    public string TimeoutAlarm = nameof(TimeoutAlarm);

    #endregion

    #region 状态

    private readonly object _actionGate = new();
    private ActionState _action;
    private long _actionStarted;

    public override ActionState ActionState
    {
        get
        {
            lock (_actionGate)
            {
                return _action;
            }
        }
    }

    /// <summary>
    /// 开着没有：挂了 Feedback 看反馈，没挂回读 DO（按 NC / NO 比）
    /// </summary>
    public bool IsOn
    {
        get
        {
            var feedback = _feedback;
            if (feedback is not null)
            {
                return feedback.IsActive == true;
            }

            var io = Io;
            if (io is null)
            {
                return false;
            }

            return io.TryReadDo(DoIndex, out bool on) && on == OpenValue;
        }
    }

    /// <summary>
    /// 开阀时 DO 写的值：常闭写 true，常开写 false
    /// </summary>
    private bool OpenValue => NormalState == NormalState.NC;

    #endregion

    #region 动作（返回 true 只表示 DO 已写进 PLC，完成看 ActionState）

    public bool On()
    {
        return Drive(true);
    }
    public bool Off()
    {
        return Drive(false);
    }

    private bool Drive(bool on)
    {
        var io = Io;
        if (io is null || DoIndex < 0)
        {
            return false;
        }

        lock (_actionGate)
        {
            // 常开阀开阀写 false、关阀写 true
            if (!io.WriteDo(DoIndex, on == OpenValue))
            {
                return false;
            }

            _actionStarted = Environment.TickCount64;
            // 只有开的那一侧有到位反馈；关回去不受控也没反馈，写完即完成。
            _action = ActionState.Completed;
            if (on && _feedback is not null)
            {
                _action = ActionState.Running;
            }

            return true;
        }
    }

    #endregion

    #region 中止

    /// <summary>
    /// 中止：不再等这次到位，输出保持原样；报警只能 Reset 清
    /// </summary>
    public override object? Abort()
    {
        base.Abort();
        lock (_actionGate)
        {
            if (_action == ActionState.Running)
            {
                _action = ActionState.Idle;
            }
        }

        return null;
    }

    #endregion

    #region 扫描：等到位、判超时

    protected override void OnScan()
    {
        base.OnScan();
        lock (_actionGate)
        {
            if (_action != ActionState.Running)
            {
                return;
            }

            if (IsOn)
            {
                _action = ActionState.Completed;
                return;
            }

            if (Environment.TickCount64 - _actionStarted < ActionTimeoutMs)
            {
                return;
            }

            _action = ActionState.Failed;
        }

        RaiseAlarm(TimeoutAlarm);
    }

    #endregion
}
