using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;


public abstract class OneStateComponent : ComponentBase
{
    #region 

    [SCEditor("-1", "IO", "驱动 DO 索引", Required = true)]
    public int DoIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "到位 DI 索引 (-1 = 没接反馈点，不等反馈也不报超时)")]
    public int DiIndex { get; set; } = -1;

    #endregion

    #region 

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "3000",
        "到位超时时间（没接 DI 时不生效）")]
    public int ActionTimeoutMs
    {
        get { return GetEcInt(nameof(ActionTimeoutMs)); }
        set { SetEcInt(nameof(ActionTimeoutMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("到位反馈超时", AlarmCategory.Timeout,
        Description = "驱动命令已发出，但在指定时间内未收到到位反馈",
        Solution = "检查气源压力、电磁阀及到位传感器；没接到位点就把 DI 索引配成 -1，"
                   + "不等也不报；超时时长在 EC ActionTimeoutMs 调")]
    public string TimeoutAlarm = nameof(TimeoutAlarm);

    #endregion

    #region 状态

    // 以下由组件扫描和调用方线程共享，访问时持有 _actionGate。
    private readonly object _actionGate = new();
    private ActionState _action;
    private long _actionStarted;

    /// <summary>
    /// 当前动作的状态：DO 写进 PLC 即 Running，到位反馈来了 Completed，超时 Failed；没接 DI 或断电回位写完即 Completed。
    /// </summary>
    public ActionState ActionState
    {
        get
        {
            lock (_actionGate)
            {
                return _action;
            }
        }
    }

    /// <summary>是否在通电到位态：接了 DI 看 DI；没接看 DO 回读（写没写进去）。PLC 没连一律 false。</summary>
    public bool IsOn
    {
        get
        {
            var io = IoComponent.Current;
            if (io is null)
            {
                return false;
            }

            if (DiIndex >= 0)
            {
                return io.TryReadDi(DiIndex, out bool arrived) && arrived;
            }

            return io.TryReadDo(DoIndex, out bool on) && on;
        }
    }

    #endregion

    #region 动作（返回 true 只表示 DO 已写进 PLC，完成看 ActionState）

    /// <summary>通电。</summary>
    public bool On()
    {
        return Drive(true);
    }

    /// <summary>断电回位。</summary>
    public bool Off()
    {
        return Drive(false);
    }

    private bool Drive(bool on)
    {
        var io = IoComponent.Current;
        if (io is null || DoIndex < 0)
        {
            return false;
        }

        lock (_actionGate)
        {
            if (!io.WriteDo(DoIndex, on))
            {
                return false;
            }

            _actionStarted = Environment.TickCount64;
            // 只有通电侧有到位反馈；断电回位不受控也没反馈，写完即完成。
            _action = on && DiIndex >= 0 ? ActionState.Running : ActionState.Completed;
            return true;
        }
    }

    #endregion

    #region 中止

    /// <summary>中止：不再等这次到位，输出保持原样；报警只能 Reset 清。</summary>
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
