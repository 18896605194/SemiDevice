using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 双状态执行器底座：两个 DO 各驱一个方向 + 两个 DI 各给一侧到位反馈。
/// 两个方向都受控，断电保持在原位。双作用气缸这类都从它派生；
/// DO/DI 的读写与到位判定写在这儿，派生类只加自己的语义属性。
/// 两侧反馈各自可选：哪一侧 DI 配成 -1，哪一侧就不等反馈（写完即完成，在不在这一侧看线圈回读），也不会因此报超时。
/// 动作由组件自己管到完成：调用方 Open()/Close() 后看 ActionState，到位超时由组件自报；新指令随时可以顶替在途动作。
/// </summary>
public abstract class TwoStateComponent : ComponentBase
{
    #region SC 装机常量

    [SCEditor("-1", "IO", "开侧驱动 DO 索引", Required = true)]
    public int DoOpenIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "关侧驱动 DO 索引", Required = true)]
    public int DoCloseIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "开到位 DI 索引 (-1 = 没接反馈点，不等反馈也不报超时)")]
    public int DiOpenedIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "关到位 DI 索引 (-1 = 没接反馈点，不等反馈也不报超时)")]
    public int DiClosedIndex { get; set; } = -1;

    #endregion

    #region EC 可调参数

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

    // 以下由组件扫描和调用方线程共享，访问时持有 _actionGate。
    private readonly object _actionGate = new();
    private ActionState _action;
    private bool _opening;
    private long _actionStarted;

    /// <summary>
    /// 当前动作的状态：DO 写进 PLC 即 Running，到位反馈来了 Completed，超时 Failed；没接反馈的一侧写完即 Completed。
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

    /// <summary>
    /// 是否在开侧：接了开到位 DI 看 DI（关到位 DI 也接了的话它还得无效——两侧同时有效是反馈坏了，不算到位）；
    /// 没接就看线圈回读，开侧通着、关侧断着算开。PLC 没连一律 false。
    /// </summary>
    public bool IsOpened => IsAtSide(DiOpenedIndex, DiClosedIndex, DoOpenIndex, DoCloseIndex);

    /// <summary>是否在关侧，判法同 <see cref="IsOpened"/>。</summary>
    public bool IsClosed => IsAtSide(DiClosedIndex, DiOpenedIndex, DoCloseIndex, DoOpenIndex);

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

    #region 动作（返回 true 只表示 DO 已写进 PLC，完成看 ActionState）

    public bool Open()
    {
        return Move(true);
    }

    public bool Close()
    {
        return Move(false);
    }

    /// <summary>先断对侧线圈再通本侧，两个线圈不会同时带电；哪一侧写失败都返回 false。</summary>
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

    /// <summary>中止：不再等这次到位，线圈保持原样（双线圈断了电反而会漂）；报警只能 Reset 清。</summary>
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
