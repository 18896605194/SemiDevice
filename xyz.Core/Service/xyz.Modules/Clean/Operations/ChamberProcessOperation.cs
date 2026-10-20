using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
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
/// 字段表里没配、这一步没填的字段就跳过（没填转速就不动卡盘，没填位置摆臂就不动）。位置是晶圆坐标，按摆臂的示教位换成轴位置；
/// 速度就是轴的设计速度，原样给轴，不按示教位换算。
/// </summary>
public sealed class ChamberProcessOperation : ChamberStepOperation
{
    // 配方字段名（sc.xml ProcessRecipe → Fields 下的节点名）：执行认这几个名字。时间是 RecipeField.SecondsKey。
    private const string RpmKey = "Rpm";
    private const string ArmKey = "Arm";
    private const string ChemicalKey = "Chemical";
    private const string FlowKey = "Flow";
    private const string ModeKey = "Mode";
    private const string PositionKey = "Position";
    private const string ScanToKey = "ScanTo";
    private const string ScanSpeedKey = "ScanSpeed";

    /// <summary>
    /// 方式字段里"来回扫"的写法（另一个是 Time）
    /// </summary>
    private const string ScanMode = "Scan";

    /// <summary>
    /// 摆臂的 Home 位：回零后轴在 0 位
    /// </summary>
    private const double HomePosition = 0;

    private const double MillisecondsPerSecond = 1000;

    /// <summary>
    /// 进度满格
    /// </summary>
    private const int FullPercent = 100;

    /// <summary>
    /// 步骤都做完、还在收尾时的进度：动作真做完才算满（做完以后站点的进度就变空了）
    /// </summary>
    private const int FinishingPercent = 99;

    private readonly BaseChamberModule _chamber;
    private readonly string _recipeName;
    private readonly IReadOnlyList<RecipeStep> _steps;

    private ChamberProcessPhase _phase;
    private int _index;

    /// <summary>
    /// 这一步的字段（没填为 null）
    /// </summary>
    private double _seconds;
    private double? _rpm;
    private double? _flow;
    private double? _position;
    private double? _scanTo;
    private double? _scanSpeed;
    private bool _isScan;

    /// <summary>
    /// 这一步用的摆臂、喷嘴；不喷为 null
    /// </summary>
    private ISwingArm? _arm;
    private INozzle? _nozzle;

    /// <summary>
    /// 上一次给卡盘的转速（一样就不重发）；还没给过为 null
    /// </summary>
    private double? _lastRpm;

    /// <summary>
    /// 这一步开始喷的时刻（本操作计时的毫秒数）；-1 = 还没开始
    /// </summary>
    private long _dwellStart = -1;

    /// <summary>
    /// Scan 下一趟往"到"走（false = 往"位置"走）
    /// </summary>
    private bool _towardScanTo;

    /// <summary>
    /// Scan 到点时已经在半路发了停轴
    /// </summary>
    private bool _scanStopped;

    /// <summary>
    /// 每一步要喷几秒（建操作时按配方快照算好）
    /// </summary>
    private readonly double[] _stepSeconds;

    /// <summary>
    /// 所有步骤加起来一共几秒
    /// </summary>
    private readonly double _totalSeconds;

    /// <summary>
    /// 进度 0~100：扫描线程每拍算一次，别的线程只读
    /// </summary>
    private volatile int _percent;

    /// <summary>
    /// 按配方快照做工艺，整个动作的上限是配方头的超时时间。
    /// </summary>
    public ChamberProcessOperation(BaseChamberModule chamber, string recipeName, RecipeData recipe)
        : base($"{chamber.Name} Process {recipeName}", chamber.Name, recipe.TimeoutMilliseconds)
    {
        _chamber = chamber;
        _recipeName = recipeName.Trim();
        _steps = recipe.Steps;
        _stepSeconds = [.. _steps.Select(RecipeData.SecondsOf)];
        _totalSeconds = _stepSeconds.Sum();
    }

    /// <summary>
    /// 工艺进度 0~100
    /// </summary>
    public int Percent => _percent;

    /// <summary>
    /// Scan 扫到一半到点了：当场停轴，停下来就进下一步（不等这一趟走完）
    /// </summary>
    protected override void OnWaiting()
    {
        UpdatePercent();

        var arm = _arm;
        if (_phase != ChamberProcessPhase.StepDwell || !_isScan || arm is null || _scanStopped || _dwellStart < 0 || !IsDue())
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

        switch (_phase)
        {
            case ChamberProcessPhase.Prepare:
                if (StopNozzles(null) && LiftsUp())
                {
                    _phase = ChamberProcessPhase.BowlUp;
                }

                break;

            case ChamberProcessPhase.BowlUp:
                if (MoveBowls(true))
                {
                    _phase = ChamberProcessPhase.StepStart;
                }

                break;

            case ChamberProcessPhase.StepStart:
                if (_index >= steps.Count)
                {
                    _phase = ChamberProcessPhase.EndNozzles;
                    break;
                }

                // 换步先关掉这一步不用的喷嘴，再改转速、摆臂：不然卡盘加减速那段上一步的药液还在喷
                if (LoadStep(steps[_index]) && StopNozzles(_nozzle))
                {
                    _phase = ChamberProcessPhase.StepSpin;
                }

                break;

            case ChamberProcessPhase.StepSpin:
                if (GiveRpm())
                {
                    _phase = ChamberProcessPhase.StepArms;
                }

                break;

            case ChamberProcessPhase.StepArms:
                if (MoveArms())
                {
                    _phase = ChamberProcessPhase.StepNozzleOn;
                }

                break;

            case ChamberProcessPhase.StepNozzleOn:
                if (OpenNozzle())
                {
                    _phase = ChamberProcessPhase.StepDwell;
                    _dwellStart = -1;
                }

                break;

            case ChamberProcessPhase.StepDwell:
                Dwell();
                break;

            case ChamberProcessPhase.EndNozzles:
                if (StopNozzles(null))
                {
                    _arm = null;
                    _phase = ChamberProcessPhase.EndArms;
                }

                break;

            case ChamberProcessPhase.EndArms:
                if (MoveArms())
                {
                    _phase = ChamberProcessPhase.EndSpin;
                }

                break;

            case ChamberProcessPhase.EndSpin:
                var spin = _chamber.SpinMotor;
                if (spin is null || Send(spin, ChamberDeviceAction.Stop, spin.Stop))
                {
                    _phase = ChamberProcessPhase.EndBowls;
                }

                break;

            case ChamberProcessPhase.EndBowls:
                if (MoveBowls(false))
                {
                    _phase = ChamberProcessPhase.Done;
                }

                break;

            default:
                Complete();
                break;
        }
    }

    /// <summary>
    /// 读这一步的字段，认出用哪条摆臂、哪个喷嘴；配方选的摆臂、药液这个腔体没有就失败（返回 false）
    /// </summary>
    private bool LoadStep(RecipeStep step)
    {
        _seconds = RecipeData.SecondsOf(step);
        _rpm = Number(step, RpmKey);
        _flow = Number(step, FlowKey);
        _position = Number(step, PositionKey);
        _scanTo = Number(step, ScanToKey);
        _scanSpeed = Number(step, ScanSpeedKey);
        var missing = FindDevices(_chamber, step, out _arm, out _nozzle);
        if (missing is not null)
        {
            return Mismatch(missing.Value.Key, missing.Value.Value);
        }

        // 来回扫要有两头；没填"到"就当 Time 停在位置上喷
        _isScan = _arm is not null
            && _position is not null
            && _scanTo is not null
            && string.Equals(step.Get(ModeKey).Trim(), ScanMode, StringComparison.OrdinalIgnoreCase);
        _scanStopped = false;
        return true;
    }

    /// <summary>
    /// 这一步选的摆臂、药液照名字在腔体里找：没选摆臂 arm 为 null（这一步不喷）；选了的找不到，回是哪个字段（字段名）、什么值。
    /// 执行时按它认设备；起工艺前、流程配方保存、Job 挑腔体也按它把每一步先查一遍（<see cref="BaseChamberModule.FindRecipeMismatch"/>）。
    /// </summary>
    public static (string Key, string Value)? FindDevices(BaseChamberModule chamber, RecipeStep step, out ISwingArm? arm, out INozzle? nozzle)
    {
        arm = null;
        nozzle = null;
        string armName = step.Get(ArmKey).Trim();
        if (armName.Length == 0)
        {
            return null;
        }

        arm = chamber.FindArm(armName);
        if (arm is null)
        {
            return (ArmKey, armName);
        }

        string chemical = step.Get(ChemicalKey).Trim();
        if (chemical.Length == 0)
        {
            return null;
        }

        nozzle = arm.FindNozzle(chemical);
        return nozzle is null ? (ChemicalKey, chemical) : null;
    }

    private bool Mismatch(string field, string value)
    {
        Fail(ErrorCodes.ChamberRecipeOptionMissing, $"{Name}：第 {_index + 1} 步的 {field} = {value}，这个腔体没有",
            _chamber.Name, _recipeName, field, value);
        return false;
    }

    /// <summary>
    /// 转速给卡盘：0 停转，别的按转速转；跟上一步一样不重发；没填转速、没配卡盘不动
    /// </summary>
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

    /// <summary>
    /// 喷嘴停液，留下 keep（这一步接着用的那个，不关了再开）
    /// </summary>
    private bool StopNozzles(INozzle? keep)
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

    /// <summary>
    /// 别的摆臂回 Home；这一步的摆臂填了位置就摆过去
    /// </summary>
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
            var nozzle = _nozzle;
            if (position is not null && !Send(arm, ChamberDeviceAction.Move, () => arm.MoveTo(arm.ToAxisPosition(position.Value, nozzle))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 开这一步的喷嘴：填了流量的先给流量（接了 AO 的下发，没接的只给流量计当监控目标），再开阀
    /// </summary>
    private bool OpenNozzle()
    {
        var nozzle = _nozzle;
        if (nozzle is null)
        {
            return true;
        }

        var flow = _flow;
        if (flow is not null && !Send(nozzle, ChamberDeviceAction.Flow, () => nozzle.SetFlow(flow.Value)))
        {
            return false;
        }

        return Send(nozzle, ChamberDeviceAction.ValveOn, nozzle.Open);
    }

    /// <summary>
    /// 喷：到点进下一步；Scan 没到点就往另一头走一趟
    /// </summary>
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
            _phase = ChamberProcessPhase.StepStart;
            return;
        }

        var arm = _arm;
        var from = _position;
        var to = _scanTo;
        if (!_isScan || arm is null || from is null || to is null)
        {
            return;
        }

        double target = arm.ToAxisPosition(_towardScanTo ? to.Value : from.Value, _nozzle);
        double? speed = _scanSpeed;
        _towardScanTo = !_towardScanTo;
        Send(arm, ChamberDeviceAction.Move, () => arm.MoveTo(target, speed));
    }

    private bool IsDue()
    {
        return Watch.ElapsedMilliseconds - _dwellStart >= _seconds * MillisecondsPerSecond;
    }

    /// <summary>
    /// 按配方每一步的时间算进度：准备段 0，收尾段 99；做完的步骤时间加上这一步已经喷了多久，除以总时间。
    /// 每一步都没填时间就按做完几步算
    /// </summary>
    private void UpdatePercent()
    {
        switch (_phase)
        {
            case ChamberProcessPhase.Prepare:
            case ChamberProcessPhase.BowlUp:
                _percent = 0;
                return;
            case ChamberProcessPhase.EndNozzles:
            case ChamberProcessPhase.EndArms:
            case ChamberProcessPhase.EndSpin:
            case ChamberProcessPhase.EndBowls:
            case ChamberProcessPhase.Done:
                _percent = FinishingPercent;
                return;
            default:
                break;
        }

        int finished = Math.Min(_index, _stepSeconds.Length);
        if (_totalSeconds <= 0)
        {
            if (_stepSeconds.Length == 0)
            {
                _percent = 0;
                return;
            }

            _percent = Math.Min(FinishingPercent, finished * FullPercent / _stepSeconds.Length);
            return;
        }

        double done = 0;
        for (int step = 0; step < finished; step++)
        {
            done += _stepSeconds[step];
        }

        if (_phase == ChamberProcessPhase.StepDwell && _dwellStart >= 0)
        {
            done += Math.Min(_seconds, (Watch.ElapsedMilliseconds - _dwellStart) / MillisecondsPerSecond);
        }

        _percent = Math.Min(FinishingPercent, (int)(done * FullPercent / _totalSeconds));
    }

    private bool LiftsUp()
    {
        foreach (var arm in _chamber.Arms)
        {
            var lift = arm.Lift;
            if (lift is not null && !Send(lift, ChamberDeviceAction.Up, lift.Up))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Bowl 升（true）或降
    /// </summary>
    private bool MoveBowls(bool up)
    {
        foreach (var bowl in _chamber.Bowls)
        {
            if (!Send(bowl, up ? ChamberDeviceAction.Up : ChamberDeviceAction.Down, up ? bowl.Up : bowl.Down))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 这一步某个数字字段的值（不变区域性）；没填、不是有限数为 null
    /// </summary>
    private static double? Number(RecipeStep step, string key)
    {
        if (double.TryParse(step.Get(key).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
        {
            return value;
        }

        return null;
    }
}
