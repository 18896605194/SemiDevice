using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 腔体按工艺配方做一次工艺（平台默认做法，照 sc 里挂的设备走）：
/// 先把喷嘴全关、Lift 升（摆臂要越过 Bowl 壁）、Bowl 升（围住盘面挡液）；然后一步一步做——
/// 先关掉这一步不用的喷嘴（卡盘加减速时不让上一步的药液接着喷），转速给卡盘；
/// 选了摆臂的，别的摆臂回 Home、这条摆到"位置"，开它上面药液对得上的喷嘴（有流量设定 AO 的先给流量）；
/// Time 停在位置上喷，Scan 在"位置"和"到"之间按"速度"来回扫；这一步时间到就进下一步，扫到一半到点了当场停轴。
/// 没选摆臂的一步不喷，摆臂都回 Home。做完喷嘴全关、摆臂回 Home、卡盘停转、Bowl 降。
/// 这一步跟上一步用同一个喷嘴就接着喷，不关了再开；转速跟上一步一样不重发。
/// 字段表里没配、这一步没填的字段就跳过（没填转速就不动卡盘，没填位置摆臂就不动）。位置是晶圆坐标，按摆臂的示教位换成轴位置。
/// </summary>
internal sealed class ChamberProcessOperation : ChamberStepOperation
{
    // 配方字段名（sc.xml ProcessRecipe → Fields 下的节点名）：执行认这几个名字。时间是 ProcessRecipeField.SecondsKey。
    private const string RpmKey = "Rpm";
    private const string ArmKey = "Arm";
    private const string ChemicalKey = "Chemical";
    private const string FlowKey = "Flow";
    private const string ModeKey = "Mode";
    private const string PositionKey = "Position";
    private const string ScanToKey = "ScanTo";
    private const string ScanSpeedKey = "ScanSpeed";

    /// <summary>方式字段里"来回扫"的写法（另一个是 Time）。</summary>
    private const string ScanMode = "Scan";

    /// <summary>摆臂的 Home 位：回零后轴在 0 位。</summary>
    private const double HomePosition = 0;

    private const double MillisecondsPerSecond = 1000;

    private enum Phase
    {
        Prepare,
        BowlUp,
        StepStart,
        StepSpin,
        StepArms,
        StepNozzleOn,
        StepDwell,
        EndNozzles,
        EndArms,
        EndSpin,
        EndBowls,
        Done,
    }

    private readonly BaseChamberModule _chamber;
    private readonly string _recipeName;
    private readonly IReadOnlyList<ProcessRecipeStep>? _steps;

    private Phase _phase;
    private int _index;

    /// <summary>这一步的字段（没填为 null）。</summary>
    private double _seconds;
    private double? _rpm;
    private double? _flow;
    private double? _position;
    private double? _scanTo;
    private double? _scanSpeed;
    private bool _isScan;

    /// <summary>这一步用的摆臂、喷嘴；不喷为 null。</summary>
    private SwingArmComponent? _arm;
    private NozzleComponent? _nozzle;

    /// <summary>上一次给卡盘的转速（一样就不重发）；还没给过为 null。</summary>
    private double? _lastRpm;

    /// <summary>这一步开始喷的时刻（本操作计时的毫秒数）；-1 = 还没开始。</summary>
    private long _dwellStart = -1;

    /// <summary>Scan 下一趟往"到"走（false = 往"位置"走）。</summary>
    private bool _towardScanTo;

    /// <summary>Scan 到点时已经在半路发了停轴。</summary>
    private bool _scanStopped;

    public ChamberProcessOperation(BaseChamberModule chamber, ProcessRequest request, int timeout)
        : base($"{chamber.Name} Process {request.RecipeName}", chamber.Name, timeout)
    {
        _chamber = chamber;
        _recipeName = request.RecipeName.Trim();
        _steps = request.Recipe?.Steps;
    }

    /// <summary>Scan 扫到一半到点了：当场停轴，停下来就进下一步（不等这一趟走完）。</summary>
    protected override void OnWaiting()
    {
        var arm = _arm;
        if (_phase != Phase.StepDwell || !_isScan || arm is null || _scanStopped || _dwellStart < 0 || !IsDue())
        {
            return;
        }

        _scanStopped = true;
        if (arm.ActionState == ActionState.Running)
        {
            Send(arm, ChamberDeviceAction.Stop, arm.Stop);
        }
    }

    protected override void Advance()
    {
        var steps = _steps;
        if (steps is null)
        {
            Fail(ErrorCodes.ChamberRecipeStepsMissing, $"{Name}：没有配方步骤（没装工艺配方库）", _chamber.Name, _recipeName);
            return;
        }

        switch (_phase)
        {
            case Phase.Prepare:
                if (StopNozzles(null) && LiftsUp())
                {
                    _phase = Phase.BowlUp;
                }

                break;

            case Phase.BowlUp:
                if (MoveBowls(true))
                {
                    _phase = Phase.StepStart;
                }

                break;

            case Phase.StepStart:
                if (_index >= steps.Count)
                {
                    _phase = Phase.EndNozzles;
                    break;
                }

                // 换步先关掉这一步不用的喷嘴，再改转速、摆臂：不然卡盘加减速那段上一步的药液还在喷
                if (LoadStep(steps[_index]) && StopNozzles(_nozzle))
                {
                    _phase = Phase.StepSpin;
                }

                break;

            case Phase.StepSpin:
                if (GiveRpm())
                {
                    _phase = Phase.StepArms;
                }

                break;

            case Phase.StepArms:
                if (MoveArms())
                {
                    _phase = Phase.StepNozzleOn;
                }

                break;

            case Phase.StepNozzleOn:
                if (OpenNozzle())
                {
                    _phase = Phase.StepDwell;
                    _dwellStart = -1;
                }

                break;

            case Phase.StepDwell:
                Dwell();
                break;

            case Phase.EndNozzles:
                if (StopNozzles(null))
                {
                    _arm = null;
                    _phase = Phase.EndArms;
                }

                break;

            case Phase.EndArms:
                if (MoveArms())
                {
                    _phase = Phase.EndSpin;
                }

                break;

            case Phase.EndSpin:
                var spin = _chamber.SpinMotor;
                if (spin is null || Send(spin, ChamberDeviceAction.Stop, spin.Stop))
                {
                    _phase = Phase.EndBowls;
                }

                break;

            case Phase.EndBowls:
                if (MoveBowls(false))
                {
                    _phase = Phase.Done;
                }

                break;

            default:
                Complete();
                break;
        }
    }

    /// <summary>读这一步的字段，认出用哪条摆臂、哪个喷嘴；配方选的摆臂、药液这个腔体没有就失败（返回 false）。</summary>
    private bool LoadStep(ProcessRecipeStep step)
    {
        _seconds = ProcessRecipeData.SecondsOf(step);
        _rpm = Number(step, RpmKey);
        _flow = Number(step, FlowKey);
        _position = Number(step, PositionKey);
        _scanTo = Number(step, ScanToKey);
        _scanSpeed = Number(step, ScanSpeedKey);
        _arm = null;
        _nozzle = null;
        string arm = step.Get(ArmKey).Trim();
        string chemical = step.Get(ChemicalKey).Trim();
        if (arm.Length > 0)
        {
            _arm = _chamber.FindArm(arm);
            if (_arm is null)
            {
                return Mismatch(ArmKey, arm);
            }

            if (chemical.Length > 0)
            {
                _nozzle = _arm.FindNozzle(chemical);
                if (_nozzle is null)
                {
                    return Mismatch(ChemicalKey, chemical);
                }
            }
        }

        // 来回扫要有两头；没填"到"就当 Time 停在位置上喷
        _isScan = _arm is not null
            && _position is not null
            && _scanTo is not null
            && string.Equals(step.Get(ModeKey).Trim(), ScanMode, StringComparison.OrdinalIgnoreCase);
        _scanStopped = false;
        return true;
    }

    private bool Mismatch(string field, string value)
    {
        Fail(ErrorCodes.ChamberRecipeOptionMissing, $"{Name}：第 {_index + 1} 步的 {field} = {value}，这个腔体没有",
            _chamber.Name, _recipeName, field, value);
        return false;
    }

    /// <summary>转速给卡盘：0 停转，别的按转速转；跟上一步一样不重发；没填转速、没配卡盘不动。</summary>
    private bool GiveRpm()
    {
        var spin = _chamber.SpinMotor;
        var rpm = _rpm;
        if (spin is null || rpm is null || rpm == _lastRpm)
        {
            return true;
        }

        _lastRpm = rpm;
        double speed = rpm.Value;
        if (speed == 0)
        {
            return Send(spin, ChamberDeviceAction.Stop, spin.Stop);
        }

        return Send(spin, ChamberDeviceAction.Spin, () => spin.Spin(speed));
    }

    /// <summary>喷嘴停液，留下 keep（这一步接着用的那个，不关了再开）。</summary>
    private bool StopNozzles(NozzleComponent? keep)
    {
        foreach (var nozzle in _chamber.Nozzles)
        {
            if (nozzle != keep && !Send(nozzle, ChamberDeviceAction.ValveOff, nozzle.Stop))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>别的摆臂回 Home；这一步的摆臂填了位置就摆过去。</summary>
    private bool MoveArms()
    {
        foreach (var arm in _chamber.Arms)
        {
            if (arm != _arm)
            {
                if (!Send(arm, ChamberDeviceAction.Move, () => arm.MoveTo(HomePosition)))
                {
                    return false;
                }

                continue;
            }

            var position = _position;
            if (position is not null && !Send(arm, ChamberDeviceAction.Move, () => arm.MoveTo(arm.ToAxisPosition(position.Value))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>开这一步的喷嘴：接了流量设定、填了流量的先给流量。</summary>
    private bool OpenNozzle()
    {
        var nozzle = _nozzle;
        if (nozzle is null)
        {
            return true;
        }

        var flow = _flow;
        if (flow is not null && nozzle.HasFlowControl && !Send(nozzle, ChamberDeviceAction.Flow, () => nozzle.SetFlow(flow.Value)))
        {
            return false;
        }

        return Send(nozzle, ChamberDeviceAction.ValveOn, nozzle.Open);
    }

    /// <summary>喷：到点进下一步；Scan 没到点就往另一头走一趟。</summary>
    private void Dwell()
    {
        if (_dwellStart < 0)
        {
            _dwellStart = Watch.ElapsedMilliseconds;
            _towardScanTo = true;
        }

        if (IsDue())
        {
            _index++;
            _phase = Phase.StepStart;
            return;
        }

        var arm = _arm;
        var from = _position;
        var to = _scanTo;
        if (!_isScan || arm is null || from is null || to is null)
        {
            return;
        }

        double target = arm.ToAxisPosition(_towardScanTo ? to.Value : from.Value);
        double? speed = _scanSpeed is null ? null : arm.ToAxisSpeed(_scanSpeed.Value);
        _towardScanTo = !_towardScanTo;
        Send(arm, ChamberDeviceAction.Move, () => arm.MoveTo(target, speed));
    }

    private bool IsDue()
    {
        return Watch.ElapsedMilliseconds - _dwellStart >= _seconds * MillisecondsPerSecond;
    }

    private bool LiftsUp()
    {
        foreach (var arm in _chamber.Arms)
        {
            var lift = arm.Lift;
            if (lift is not null && !Send(lift, ChamberDeviceAction.Up, lift.Open))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Bowl 升（true）或降。</summary>
    private bool MoveBowls(bool up)
    {
        foreach (var bowl in _chamber.Bowls)
        {
            if (!Send(bowl, up ? ChamberDeviceAction.Up : ChamberDeviceAction.Down, up ? bowl.Open : bowl.Close))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>这一步某个数字字段的值（不变区域性）；没填、不是有限数为 null。</summary>
    private static double? Number(ProcessRecipeStep step, string key)
    {
        if (double.TryParse(step.Get(key).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
        {
            return value;
        }

        return null;
    }
}
