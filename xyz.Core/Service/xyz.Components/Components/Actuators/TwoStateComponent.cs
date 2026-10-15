using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 双状态执行器底座：两个 DO 各驱一个方向 + 两个 DI 各给一侧到位反馈。
/// </summary>
public abstract class TwoStateComponent : ComponentBase
{
    #region SC 

    [SCEditor("-1", "IO", "开侧驱动 DO 索引", Required = true)]
    public int DoOpenIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "关侧驱动 DO 索引", Required = true)]
    public int DoCloseIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "开到位 DI 索引 (-1 = 没接反馈点，不等反馈也不报超时)")]
    public int DiOpenedIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "关到位 DI 索引 (-1 = 没接反馈点，不等反馈也不报超时)")]
    public int DiClosedIndex { get; set; } = -1;

    #endregion

    #region EC 

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "5000",
        "到位超时时间（只管接了 DI 的那一侧）")]
    public int ActionTimeoutMs
    {
        get { return GetEcInt(nameof(ActionTimeoutMs)); }
        set { SetEcInt(nameof(ActionTimeoutMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("开/关到位超时", AlarmCategory.Timeout,
        Description = "开/关命令已发出，但在指定时间内未收到该侧到位反馈",
        Solution = "检查气源压力、电磁阀及到位传感器；没接到位点的那一侧把 DI 索引配成 -1，"
                   + "不等也不报；超时时长在 EC ActionTimeoutMs 调")]
    public string TimeoutAlarm = nameof(TimeoutAlarm);

    #endregion

    #region 状态


    private readonly object _actionGate = new();
    private ActionState _action;
    private bool _opening;
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


    public bool IsOpened => IsAtSide(DiOpenedIndex, DiClosedIndex, DoOpenIndex, DoCloseIndex);

    public bool IsClosed => IsAtSide(DiClosedIndex, DiOpenedIndex, DoCloseIndex, DoOpenIndex);

    public TwoStatePosition Position
    {
        get
        {
            var io = IoComponent.Current;
            if (io is null || !io.TryReadDo(DoOpenIndex, out bool open) || !io.TryReadDo(DoCloseIndex, out bool close))
            {
                return TwoStatePosition.Unknown;
            }

            if (open != close)
            {
                if (open)
                {
                    return IsOpened ? TwoStatePosition.Opened : TwoStatePosition.Unknown;
                }

                return IsClosed ? TwoStatePosition.Closed : TwoStatePosition.Unknown;
            }

            if (IsOpened)
            {
                return TwoStatePosition.Opened;
            }

            return IsClosed ? TwoStatePosition.Closed : TwoStatePosition.Unknown;
        }
    }

    /// <summary>
    /// 配置了di就回读di，没有配置就不回读
    /// </summary>
    /// <param name="di"></param>
    /// <param name="oppositeDi"></param>
    /// <param name="coil"></param>
    /// <param name="oppositeCoil"></param>
    /// <returns></returns>
    private static bool IsAtSide(int di, int oppositeDi, int coil, int oppositeCoil)
    {
        var io = IoComponent.Current;
        if (io is null)
        {
            return false;
        }

        if (di < 0)
        {
            return io.TryReadDo(coil, out bool on) && on
                && io.TryReadDo(oppositeCoil, out bool oppositeOn) && !oppositeOn;
        }

        if (!io.TryReadDi(di, out bool arrived) || !arrived)
        {
            return false;
        }

        return oppositeDi < 0 || (io.TryReadDi(oppositeDi, out bool oppositeArrived) && !oppositeArrived);
    }

    #endregion

    #region 动作

    public bool Open()
    {
        return Move(true);
    }

    public bool Close()
    {
        return Move(false);
    }

    private bool Move(bool opening)
    {
        var io = IoComponent.Current;
        int coil = opening ? DoOpenIndex : DoCloseIndex;
        int oppositeCoil = opening ? DoCloseIndex : DoOpenIndex;
        if (io is null || coil < 0 || oppositeCoil < 0)
        {
            return false;
        }

        lock (_actionGate)
        {
            if (!io.WriteDo(oppositeCoil, false) || !io.WriteDo(coil, true))
            {
                return false;
            }

            _opening = opening;
            _actionStarted = Environment.TickCount64;
            // 这一侧没接到位点就不等了，写完即完成。
            _action = (opening ? DiOpenedIndex : DiClosedIndex) < 0 ? ActionState.Completed : ActionState.Running;
            return true;
        }
    }

    #endregion

    #region 中止

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

            if (_opening ? IsOpened : IsClosed)
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
