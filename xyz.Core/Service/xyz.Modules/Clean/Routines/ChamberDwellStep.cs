using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 工艺里配方一步的"喷"：Time 停在位置上喷够时间；Scan 在"位置"和"到"之间按"速度"来回扫，时间到了扫到一半也当场停轴，停下来才算这一步做完
/// </summary>
internal sealed class ChamberDwellStep : RoutineStep
{
    private const double MillisecondsPerSecond = 1000;

    private readonly double _seconds;
    private readonly ISwingArm? _arm;
    private readonly INozzle? _nozzle;

    /// <summary>
    /// 来回扫的两头（晶圆坐标）、速度；不扫为 null
    /// </summary>
    private readonly double? _from;
    private readonly double? _to;
    private readonly double? _speed;

    /// <summary>
    /// 下一趟往"到"走（false = 往"位置"走）
    /// </summary>
    private bool _towardTo = true;

    /// <summary>
    /// 来回扫发过一趟了：之后摆臂回到 Idle 算被中止
    /// </summary>
    private bool _moved;

    /// <summary>
    /// 时间到了扫到一半，已经发了停轴
    /// </summary>
    private bool _stopped;

    /// <summary>
    /// 开始喷了（进度在别的线程读）
    /// </summary>
    private volatile bool _started;

    /// <summary>
    /// 停在位置上喷（from、to 为 null）或来回扫
    /// </summary>
    public ChamberDwellStep(string name, double seconds, ISwingArm? arm, INozzle? nozzle, double? from, double? to, double? speed)
        : base(name)
    {
        _seconds = seconds;
        _arm = arm;
        _nozzle = nozzle;
        _from = from;
        _to = to;
        _speed = speed;
    }

    /// <summary>
    /// 这一步喷了几秒（还没开始为 0，最多到这一步的时间）
    /// </summary>
    public double DwelledSeconds
    {
        get
        {
            if (!_started)
            {
                return 0;
            }

            return Math.Min(_seconds, ElapsedMs / MillisecondsPerSecond);
        }
    }

    /// <summary>
    /// 来回扫时在等摆臂
    /// </summary>
    public override string Waiting
    {
        get
        {
            var arm = _arm;
            if (arm is null || arm.ActionState != ActionState.Running)
            {
                return string.Empty;
            }

            return arm.FullPath;
        }
    }

    protected internal override void Run()
    {
        _started = true;
    }

    protected internal override bool Check()
    {
        bool due = ElapsedMs >= _seconds * MillisecondsPerSecond;
        var arm = _arm;
        var from = _from;
        var to = _to;
        if (arm is null || from is null || to is null)
        {
            return due;
        }

        var action = ChamberDeviceAction.Move;
        if (_stopped)
        {
            action = ChamberDeviceAction.Stop;
        }

        switch (arm.ActionState)
        {
            case ActionState.Failed:
                Fail(ErrorCodes.ChamberDeviceActionFailed, $"{arm.FullPath} {action} 没做成", arm.FullPath, action.ToString());
                return false;

            case ActionState.Idle:
                if (_moved)
                {
                    Fail(ErrorCodes.Aborted, $"{arm.FullPath} 被中止", Routine.Name);
                    return false;
                }

                break;

            case ActionState.Running:
                // 到点了扫到一半：当场停轴，停下来再进下一步（不等这一趟走完）
                if (due && !_stopped)
                {
                    _stopped = true;
                    if (!arm.Stop())
                    {
                        Fail(ErrorCodes.ChamberDeviceCommandRejected, $"{arm.FullPath} Stop 指令没发出去", arm.FullPath, nameof(ChamberDeviceAction.Stop));
                    }
                }

                return false;

            default:
                break;
        }

        if (due)
        {
            return true;
        }

        // 这一趟走完了，往另一头走一趟
        double target = arm.ToAxisPosition(NextEnd(from.Value, to.Value), _nozzle);
        _moved = true;
        if (!arm.MoveTo(target, _speed))
        {
            Fail(ErrorCodes.ChamberDeviceCommandRejected,
                $"{arm.FullPath} Move 到 {target.ToString(CultureInfo.InvariantCulture)} 指令没发出去", arm.FullPath, nameof(ChamberDeviceAction.Move));
        }

        return false;
    }

    /// <summary>
    /// 下一趟去哪一头：先往"到"，再回"位置"，轮着来
    /// </summary>
    private double NextEnd(double from, double to)
    {
        bool towardTo = _towardTo;
        _towardTo = !towardTo;
        if (towardTo)
        {
            return to;
        }

        return from;
    }
}
