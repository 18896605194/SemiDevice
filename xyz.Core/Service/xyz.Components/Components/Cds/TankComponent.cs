using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Configs.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Components.Components;

/// <summary>
///Tank
/// </summary>
[Component(description: "供液的槽（子节点 LL / L / H / HH / Overfill 挂液位信号：DI 信号或读液位计的 AI 液位信号；Fill / Drain / Supply 挂阀组；没挂 = 没有这个点、没有这个功能）")]
public class TankComponent : ComponentBase, ITank
{
    #region 液位信号

    private IDiSensor? _ll;

    private IDiSensor? _l;

    private IDiSensor? _h;

    private IDiSensor? _hh;

    private IDiSensor? _overfill;

    /// <summary>
    /// 五个信号从下往上排，没装的是 null
    /// </summary>
    private IDiSensor?[] _signals = [];

    /// <summary>
    /// LL 信号，没配是 null
    /// </summary>
    public IDiSensor? LL => _ll;

    /// <summary>
    /// L 信号，没配是 null
    /// </summary>
    public IDiSensor? L => _l;

    /// <summary>
    /// H 信号，没配是 null
    /// </summary>
    public IDiSensor? H => _h;

    /// <summary>
    /// HH 信号，没配是 null
    /// </summary>
    public IDiSensor? HH => _hh;

    /// <summary>
    /// Overfill 信号，没配是 null
    /// </summary>
    public IDiSensor? Overfill => _overfill;

    #endregion

    /// <summary>
    /// machine.xml FillTo 指的那个
    /// </summary>
    private IDiSensor? _fillTo;

    #region 阀组

    private IValveGroup? _fill;

    private IValveGroup? _drain;

    private IValveGroup? _supply;

    #endregion

    #region Machine

    [Machine("H", "Tank", "补液补到哪个信号亮算完成：L 或 H（HH 是报警位，不能当完成位）")]
    public string FillTo { get; set; } = "H";

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "60000", "3000", "液位信号异常防抖：上面亮了、下面没亮持续满该时长才报警")]
    public int LevelSensorDebounceMs
    {
        get { return GetEcInt(nameof(LevelSensorDebounceMs)); }
        set { SetEcInt(nameof(LevelSensorDebounceMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "60000", "1000", "液位高高防抖：HH 亮着持续满该时长才报警")]
    public int HighHighDebounceMs
    {
        get { return GetEcInt(nameof(HighHighDebounceMs)); }
        set { SetEcInt(nameof(HighHighDebounceMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "60000", "1000", "溢流防抖：Overfill 亮着持续满该时长才报警")]
    public int OverfillDebounceMs
    {
        get { return GetEcInt(nameof(OverfillDebounceMs)); }
        set { SetEcInt(nameof(OverfillDebounceMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "60000", "1000", "供液低低防抖：供液中 LL 灭了持续满该时长就报警（报警了槽进出错、阀全关）")]
    public int SupplyLowLowDebounceMs
    {
        get { return GetEcInt(nameof(SupplyLowLowDebounceMs)); }
        set { SetEcInt(nameof(SupplyLowLowDebounceMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "1000", "3600000", "600000", "补液超时：补液阀开了这么久还没到 H 就报警")]
    public int FillTimeoutMs
    {
        get { return GetEcInt(nameof(FillTimeoutMs)); }
        set { SetEcInt(nameof(FillTimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "1000", "3600000", "600000", "排液超时：排液阀开了这么久 LL 还没灭就报警")]
    public int DrainTimeoutMs
    {
        get { return GetEcInt(nameof(DrainTimeoutMs)); }
        set { SetEcInt(nameof(DrainTimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "600000", "10000", "排空延时：排液时 LL 灭了以后再排这么久才关阀")]
    public int DrainDelayMs
    {
        get { return GetEcInt(nameof(DrainDelayMs)); }
        set { SetEcInt(nameof(DrainDelayMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("液位信号异常", AlarmCategory.IrrecoverableError,
        Description = "上面的液位信号到了、下面的却没到",
        Solution = "检查液位信号有没有卡住、接线和触发值配置")]
    public string LevelSensorAlarm = nameof(LevelSensorAlarm);

    [Alarm("液位高高", AlarmCategory.ParameterControlError,
        Description = "HH 信号亮了",
        Solution = "检查补液阀有没有关住、H 信号是否失灵")]
    public string HighHighAlarm = nameof(HighHighAlarm);

    [Alarm("溢流", AlarmCategory.EquipmentSafety,
        Description = "Overfill 信号亮了，槽里的液已经满出来了",
        Solution = "停止补液，检查槽和接液盘")]
    public string OverfillAlarm = nameof(OverfillAlarm);

    [Alarm("补液超时", AlarmCategory.IrrecoverableError,
        Description = "补液阀开了 EC FillTimeoutMs 还没到 H",
        Solution = "检查补液阀、来液压力和 H 信号")]
    public string FillTimeoutAlarm = nameof(FillTimeoutAlarm);

    [Alarm("排液超时", AlarmCategory.IrrecoverableError,
        Description = "排液阀开了 EC DrainTimeoutMs LL 还没灭",
        Solution = "检查排液阀、排液管路和 LL 信号")]
    public string DrainTimeoutAlarm = nameof(DrainTimeoutAlarm);

    [Alarm("供液中液位低低", AlarmCategory.ParameterControlError,
        Description = "供液时液位掉到 LL 以下，已停止供液",
        Solution = "补液后复位")]
    public string SupplyLowLowAlarm = nameof(SupplyLowLowAlarm);

    #endregion

    #region SV

    /// <summary>
    /// 液位：照配了的液位信号算
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "液位")]
    public TankLevel Level { get; private set; }

    /// <summary>
    /// 槽在做什么
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Enum, description: "槽状态")]
    public TankState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    #endregion

    #region 状态

    private readonly object _gate = new();

    private TankState _state;

    /// <summary>
    /// 补液 / 排液这一段开阀的时刻
    /// </summary>
    private long _stepSince;

    /// <summary>
    /// 排液时 LL 灭下去的时刻，0 = 还没灭
    /// </summary>
    private long _emptySince;

    /// <summary>
    /// 换液走到补液那一段了
    /// </summary>
    private bool _exchangeFilling;

    #endregion

    /// <summary>
    /// machine.xml 灌完值：补液完成位只能是 L 或 H，配错开机就报
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (string.Equals(FillTo, "L", StringComparison.OrdinalIgnoreCase))
        {
            FillTo = "L";
            return;
        }

        if (string.Equals(FillTo, "H", StringComparison.OrdinalIgnoreCase))
        {
            FillTo = "H";
            return;
        }

        throw new InvalidOperationException($"{FullPath} 的 machine.xml FillTo 配成了 \"{FillTo}\"，只能是 L 或 H");
    }

    /// <summary>
    /// 照子节点名认液位信号和阀组；槽里本来有液也从空闲开始，要补一次液才就绪
    /// </summary>
    public override bool InitComponent()
    {
        _ll = FindSignal("LL");
        _l = FindSignal("L");
        _h = FindSignal("H");
        _hh = FindSignal("HH");
        _overfill = FindSignal("Overfill");
        _signals = [_ll, _l, _h, _hh, _overfill];
        _fillTo = FindSignal(FillTo);
        _fill = FindChild<IValveGroup>("Fill");
        _drain = FindChild<IValveGroup>("Drain");
        _supply = FindChild<IValveGroup>("Supply");
        return base.InitComponent();
    }

    #region 功能

    /// <summary>
    /// 补液：开补液阀，FillTo 亮了关阀、就绪；本来就亮着不开阀，直接就绪
    /// </summary>
    public HandleResult Fill()
    {
        lock (_gate)
        {
            var rejected = CheckFree();
            if (rejected is not null)
            {
                return rejected;
            }

            var fill = _fill;
            if (fill is null)
            {
                return HandleResult.Fail(ErrorCodes.TankValveGroupMissing, FullPath, "Fill");
            }

            if (_fillTo is null)
            {
                return HandleResult.Fail(ErrorCodes.TankSignalMissing, FullPath, FillTo);
            }

            if (Level == TankLevel.Unknown)
            {
                return HandleResult.Fail(ErrorCodes.TankLevelUnknown, FullPath);
            }

            if (IsOn(_hh) || IsOn(_overfill))
            {
                return HandleResult.Fail(ErrorCodes.TankHighHigh, FullPath);
            }

            if (IsOn(_fillTo))
            {
                SetState(TankState.Ready, $"补液：{FillTo} 已经亮了，不开阀");
                return HandleResult.Success();
            }

            return Begin(TankState.Filling, fill, "Fill");
        }
    }

    /// <summary>
    /// 排液：开排液阀，LL 灭了再排 EC DrainDelayMs 关阀、回空闲
    /// </summary>
    public HandleResult Drain()
    {
        lock (_gate)
        {
            var rejected = CheckFree();
            if (rejected is not null)
            {
                return rejected;
            }

            var drain = _drain;
            if (drain is null)
            {
                return HandleResult.Fail(ErrorCodes.TankValveGroupMissing, FullPath, "Drain");
            }

            if (_ll is null)
            {
                return HandleResult.Fail(ErrorCodes.TankSignalMissing, FullPath, "LL");
            }

            return Begin(TankState.Draining, drain, "Drain");
        }
    }

    /// <summary>
    /// 换液：先排液、排空了接着补液，补到 H 就绪
    /// </summary>
    public HandleResult Exchange()
    {
        lock (_gate)
        {
            var rejected = CheckFree();
            if (rejected is not null)
            {
                return rejected;
            }

            var drain = _drain;
            if (drain is null)
            {
                return HandleResult.Fail(ErrorCodes.TankValveGroupMissing, FullPath, "Drain");
            }

            if (_fill is null)
            {
                return HandleResult.Fail(ErrorCodes.TankValveGroupMissing, FullPath, "Fill");
            }

            if (_ll is null)
            {
                return HandleResult.Fail(ErrorCodes.TankSignalMissing, FullPath, "LL");
            }

            if (_fillTo is null)
            {
                return HandleResult.Fail(ErrorCodes.TankSignalMissing, FullPath, FillTo);
            }

            return Begin(TankState.Exchanging, drain, "Drain");
        }
    }

    /// <summary>
    /// 供液：开供液阀，一直开着，直到停止；只能从就绪开始
    /// </summary>
    public HandleResult Supply()
    {
        lock (_gate)
        {
            var rejected = CheckFree();
            if (rejected is not null)
            {
                return rejected;
            }

            var supply = _supply;
            if (supply is null)
            {
                return HandleResult.Fail(ErrorCodes.TankValveGroupMissing, FullPath, "Supply");
            }

            if (_state != TankState.Ready)
            {
                return HandleResult.Fail(ErrorCodes.TankNotReady, FullPath);
            }

            return Begin(TankState.Supplying, supply, "Supply");
        }
    }

    /// <summary>
    /// 停止：关掉全部阀，直接回空闲（有报警的下一拍又进出错）
    /// </summary>
    public HandleResult Stop()
    {
        lock (_gate)
        {
            bool closed = CloseAll();
            SetState(TankState.Idle, "停止");
            if (!closed)
            {
                return HandleResult.Fail(ErrorCodes.TankCloseFailed, FullPath);
            }

            return HandleResult.Success();
        }
    }

    /// <summary>
    /// 中止（上面的模块、整机停下来时顺着设备树带过来）：跟停止一样
    /// </summary>
    public override object? Abort()
    {
        base.Abort();
        Stop();
        return null;
    }

    /// <summary>
    /// 复位：清掉自己和下面的报警，出错回空闲；报警条件还在的下一拍又会报、又进出错
    /// </summary>
    public override object? Reset()
    {
        base.Reset();
        lock (_gate)
        {
            if (_state == TankState.Error)
            {
                SetState(TankState.Idle, "复位");
            }
        }

        return null;
    }

    /// <summary>
    /// 出错了要先复位；在补液、排液、换液、供液就先停止
    /// </summary>
    private HandleResult? CheckFree()
    {
        if (_state == TankState.Error)
        {
            return HandleResult.Fail(ErrorCodes.TankError, FullPath);
        }

        if (_state != TankState.Idle && _state != TankState.Ready)
        {
            return HandleResult.Fail(ErrorCodes.TankBusy, FullPath);
        }

        return null;
    }

    /// <summary>
    /// 开这一组阀、进这个状态；有阀没开成就全关回去
    /// </summary>
    private HandleResult Begin(TankState state, IValveGroup group, string groupName)
    {
        if (!group.Open())
        {
            CloseAll();
            return HandleResult.Fail(ErrorCodes.TankValveFailed, FullPath, groupName);
        }

        _stepSince = Stopwatch.GetTimestamp();
        _emptySince = 0;
        _exchangeFilling = false;
        SetState(state, $"开 {groupName} 阀组");
        return HandleResult.Success();
    }

    #endregion

    #region 扫描

    /// <summary>
    /// 先让信号读 DI、防抖，再算液位、报警，最后推正在做的那个功能
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();

        bool mismatch = IsMismatch();
        Level = ReadLevel(mismatch);

        // 条件一直成立满各自的防抖时间才报，中间断了重新计时
        CheckAlarm(LevelSensorAlarm, mismatch, LevelSensorDebounceMs);
        CheckAlarm(HighHighAlarm, IsOn(_hh), HighHighDebounceMs);
        CheckAlarm(OverfillAlarm, IsOn(_overfill), OverfillDebounceMs);

        lock (_gate)
        {
            // 供液中 LL 灭了；每拍都要算，不在供液时把计时清掉
            bool dry = _state == TankState.Supplying && _ll is not null && _ll.IsActive == false;
            CheckAlarm(SupplyLowLowAlarm, dry, SupplyLowLowDebounceMs);

            switch (_state)
            {
                case TankState.Ready:
                    if (!LevelReady())
                    {
                        SetState(TankState.Idle, $"液位 {Level}，不再就绪");
                    }

                    break;
                case TankState.Filling:
                    ScanFill();
                    break;
                case TankState.Draining:
                    ScanDrain();
                    break;
                case TankState.Exchanging:
                    if (_exchangeFilling)
                    {
                        ScanFill();
                    }
                    else
                    {
                        ScanDrain();
                    }

                    break;
            }

            // 槽或下面的信号、阀有任何报警就是出错：阀全关，复位清了报警才回空闲
            if (_state != TankState.Error && HasAlarm)
            {
                CloseAll();
                SetState(TankState.Error, "有报警");
            }
        }
    }

    /// <summary>
    /// 补液这一段：FillTo 亮了关阀就绪；HH 或溢流亮了当场关阀回空闲（报警满防抖时间再进出错）；超时报警
    /// </summary>
    private void ScanFill()
    {
        if (IsOn(_hh) || IsOn(_overfill))
        {
            CloseGroup(_fill, "Fill");
            SetState(TankState.Idle, "补液时 HH 或溢流亮了，关补液阀");
            return;
        }

        if (IsOn(_fillTo))
        {
            CloseGroup(_fill, "Fill");
            if (LevelReady())
            {
                SetState(TankState.Ready, $"补到 {FillTo} 了");
                return;
            }

            SetState(TankState.Idle, $"补到 {FillTo} 了，但液位信号不对，不算就绪");
            return;
        }

        if (Elapsed(_stepSince) >= FillTimeoutMs)
        {
            RaiseAlarm(FillTimeoutAlarm);
        }
    }

    /// <summary>
    /// 排液这一段：LL 灭了再排一会儿关阀；换液接着补液，不然回空闲；LL 一直不灭报超时
    /// </summary>
    private void ScanDrain()
    {
        if (IsOn(_ll))
        {
            // 液面又碰到 LL（晃）：重新等
            _emptySince = 0;
        }
        else if (_emptySince == 0 && _ll is not null && _ll.IsActive == false)
        {
            _emptySince = Stopwatch.GetTimestamp();
        }

        if (_emptySince == 0)
        {
            if (Elapsed(_stepSince) >= DrainTimeoutMs)
            {
                RaiseAlarm(DrainTimeoutAlarm);
            }

            return;
        }

        if (Elapsed(_emptySince) < DrainDelayMs)
        {
            return;
        }

        CloseGroup(_drain, "Drain");
        if (_state != TankState.Exchanging)
        {
            SetState(TankState.Idle, "排液完成");
            return;
        }

        // 换液：排完了接着补
        var fill = _fill;
        if (fill is null || !fill.Open())
        {
            CloseAll();
            SetState(TankState.Idle, "换液：补液阀开不了");
            return;
        }

        _exchangeFilling = true;
        _stepSince = Stopwatch.GetTimestamp();
        LogHelper.Info(FullPath, "换液：排完了，开始补液");
    }

    #endregion

    #region 阀、状态

    /// <summary>
    /// 三组阀全关；有没关上的返回 false
    /// </summary>
    private bool CloseAll()
    {
        bool closed = CloseGroup(_fill, "Fill");
        closed = CloseGroup(_drain, "Drain") && closed;
        closed = CloseGroup(_supply, "Supply") && closed;
        return closed;
    }

    /// <summary>
    /// 关一组阀；没配的算关好了，没关上记日志
    /// </summary>
    private bool CloseGroup(IValveGroup? group, string groupName)
    {
        if (group is null || group.Close())
        {
            return true;
        }

        LogHelper.Warn(FullPath, $"{groupName} 阀组没关上（PLC 没连上或 DO 点没配）");
        return false;
    }

    private void SetState(TankState state, string reason)
    {
        if (_state == state)
        {
            return;
        }

        if (state == TankState.Error)
        {
            LogHelper.Warn(FullPath, $"{_state} → {state}：{reason}");
        }
        else
        {
            LogHelper.Info(FullPath, $"{_state} → {state}：{reason}");
        }

        _state = state;
    }

    /// <summary>
    /// 液位够不够就绪：L 以上、没到 HH
    /// </summary>
    private bool LevelReady()
    {
        return Level == TankLevel.Normal || Level == TankLevel.H;
    }

    private static double Elapsed(long since)
    {
        return Stopwatch.GetElapsedTime(since).TotalMilliseconds;
    }

    #endregion

    #region 液位

    /// <summary>
    /// 从上往下看亮到了哪个信号
    /// </summary>
    private TankLevel ReadLevel(bool mismatch)
    {
        // Overfill 亮了就是已经满出来了，别的信号读不到、对不上也是溢流
        if (IsOn(_overfill))
        {
            return TankLevel.Overfill;
        }

        if (CannotRead() || mismatch)
        {
            return TankLevel.Unknown;
        }

        if (IsOn(_hh))
        {
            return TankLevel.HH;
        }

        if (IsOn(_h))
        {
            return TankLevel.H;
        }

        if (IsOn(_l))
        {
            return TankLevel.Normal;
        }

        if (IsOn(_ll))
        {
            return TankLevel.L;
        }

        // 一个都没亮：只能说低于装了的最低那个
        if (_ll is not null)
        {
            return TankLevel.LL;
        }

        if (_l is not null)
        {
            return TankLevel.L;
        }

        if (_h is not null)
        {
            return TankLevel.Normal;
        }

        if (_hh is not null)
        {
            return TankLevel.H;
        }

        return TankLevel.Unknown;
    }

    /// <summary>
    /// 上面的信号亮了、下面的却没亮
    /// </summary>
    private bool IsMismatch()
    {
        if (CannotRead())
        {
            return false;
        }

        // 从下往上，碰到没亮的以后又碰到亮的就是对不上
        bool sawOff = false;
        foreach (var signal in _signals)
        {
            if (signal is null)
            {
                continue;
            }

            if (signal.IsActive == true)
            {
                if (sawOff)
                {
                    return true;
                }
            }
            else
            {
                sawOff = true;
            }
        }

        return false;
    }

    /// <summary>
    /// 装了的信号有读不到的
    /// </summary>
    private bool CannotRead()
    {
        foreach (var signal in _signals)
        {
            if (signal is not null && signal.IsActive is null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 装了、读得到、亮着
    /// </summary>
    private static bool IsOn(IDiSensor? signal)
    {
        return signal is not null && signal.IsActive == true;
    }

    #endregion
}
