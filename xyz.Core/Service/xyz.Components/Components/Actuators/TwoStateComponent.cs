using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 双状态执行器底座：两个 DO 各驱一个方向，两侧到位反馈挂子节点 Opened / Closed（DiSensorComponent），没挂的那侧不等反馈
/// </summary>
public abstract class TwoStateComponent : ComponentBase
{
    #region SC

    [SCEditor("-1", "IO", "开侧驱动 DO 索引", Required = true)]
    public int DoOpenIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "关侧驱动 DO 索引", Required = true)]
    public int DoCloseIndex { get; set; } = -1;

    #endregion

    #region 到位反馈

    /// <summary>
    /// 开到位（子节点 Opened）；没挂是 null
    /// </summary>
    private IDiSensor? _opened;

    /// <summary>
    /// 关到位（子节点 Closed）；没挂是 null
    /// </summary>
    private IDiSensor? _closed;

    public override bool InitComponent()
    {
        _opened = FindSignal("Opened");
        _closed = FindSignal("Closed");
        return base.InitComponent();
    }

    #endregion

    #region EC 

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "5000",
        "到位超时时间（只管挂了到位反馈的那一侧）")]
    public int ActionTimeoutMs
    {
        get { return GetEcInt(nameof(ActionTimeoutMs)); }
        set { SetEcInt(nameof(ActionTimeoutMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("开/关到位超时", AlarmCategory.Timeout,
        Description = "开/关命令已发出，但在指定时间内未收到该侧到位反馈",
        Solution = "检查气源压力、电磁阀及到位传感器；没接到位点的那一侧就不挂 Opened / Closed 子节点，"
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


    public bool IsOpened => IsAtSide(_opened, _closed, DoOpenIndex, DoCloseIndex);

    public bool IsClosed => IsAtSide(_closed, _opened, DoCloseIndex, DoOpenIndex);

    public TwoStatePosition Position
    {
        get
        {
            var io = IoComponent.Current;
            if (io is null || !io.TryReadDo(DoOpenIndex, out bool open) || !io.TryReadDo(DoCloseIndex, out bool close))
            {
                return TwoStatePosition.Unknown;
            }

            // 只通了一侧线圈：只认这一侧到位
            if (open && !close)
            {
                if (IsOpened)
                {
                    return TwoStatePosition.Opened;
                }

                return TwoStatePosition.Unknown;
            }

            if (close && !open)
            {
                if (IsClosed)
                {
                    return TwoStatePosition.Closed;
                }

                return TwoStatePosition.Unknown;
            }

            if (IsOpened)
            {
                return TwoStatePosition.Opened;
            }

            if (IsClosed)
            {
                return TwoStatePosition.Closed;
            }

            return TwoStatePosition.Unknown;
        }
    }

    /// <summary>
    /// 到没到这一侧：这侧挂了反馈看反馈（对面挂了还要对面没亮），没挂看这侧线圈通、对面线圈断
    /// </summary>
    private static bool IsAtSide(IDiSensor? side, IDiSensor? opposite, int coil, int oppositeCoil)
    {
        if (side is not null)
        {
            return side.IsActive == true && (opposite is null || opposite.IsActive == false);
        }

        var io = IoComponent.Current;
        if (io is null)
        {
            return false;
        }

        return io.TryReadDo(coil, out bool on) && on
            && io.TryReadDo(oppositeCoil, out bool oppositeOn) && !oppositeOn;
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
        int coil = DoCloseIndex;
        int oppositeCoil = DoOpenIndex;
        var feedback = _closed;
        if (opening)
        {
            coil = DoOpenIndex;
            oppositeCoil = DoCloseIndex;
            feedback = _opened;
        }

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
            // 这一侧没挂到位反馈就不等了，写完即完成。
            _action = ActionState.Running;
            if (feedback is null)
            {
                _action = ActionState.Completed;
            }

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

            bool arrived = IsClosed;
            if (_opening)
            {
                arrived = IsOpened;
            }

            if (arrived)
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
