using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Modules;

[Component(description: "E84 交接组件（IO 版）")]
public class E84Component : ComponentBase, IE84
{
    #region SC

    [SCEditor("-1", "IO", "VALID 输入 DI 索引")]
    public int DiValidIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "CS_0 输入 DI 索引")]
    public int DiCs0Index { get; set; } = -1;

    [SCEditor("-1", "IO", "CS_1 输入 DI 索引")]
    public int DiCs1Index { get; set; } = -1;

    [SCEditor("-1", "IO", "AM_AVBL 输入 DI 索引")]
    public int DiAmAvblIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "TR_REQ 输入 DI 索引")]
    public int DiTrReqIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "BUSY 输入 DI 索引")]
    public int DiBusyIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "COMPT 输入 DI 索引")]
    public int DiComptIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "CONT 输入 DI 索引")]
    public int DiContIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "光幕输入 DI 索引（-1 = 没有光幕，不做光幕互锁）")]
    public int DiLightCurtainIndex { get; set; } = -1;

    [SCEditor("False", "IO", "光幕电平：False = 输入 OFF 算被挡，True = 输入 ON 算被挡")]
    public bool LightCurtainReverse { get; set; }

    [SCEditor("-1", "IO", "L_REQ 输出 DO 索引")]
    public int DoLReqIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "U_REQ 输出 DO 索引")]
    public int DoUReqIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "READY 输出 DO 索引")]
    public int DoReadyIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "HO_AVBL 输出 DO 索引")]
    public int DoHoAvblIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "ES 输出 DO 索引")]
    public int DoEsIndex { get; set; } = -1;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Bool, @default: "False",
        description: "是否启用 E84（False = 不理搬运车，输出全灭）")]
    public bool E84Enabled
    {
        get { return bool.TryParse(GetEcString(nameof(E84Enabled)), out var enabled) && enabled; }
        set { SetEc(nameof(E84Enabled), value.ToString()); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "2000", description: "TP1：亮 L_REQ/U_REQ 后等搬运车 TR_REQ")]
    public int Tp1Timeout
    {
        get { return GetEcInt(nameof(Tp1Timeout)); }
        set { SetEcInt(nameof(Tp1Timeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "2000", description: "TP2：给出 READY 后等搬运车 BUSY")]
    public int Tp2Timeout
    {
        get { return GetEcInt(nameof(Tp2Timeout)); }
        set { SetEcInt(nameof(Tp2Timeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "60000", description: "TP3：BUSY 后等载具放上/取走")]
    public int Tp3Timeout
    {
        get { return GetEcInt(nameof(Tp3Timeout)); }
        set { SetEcInt(nameof(Tp3Timeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "60000", description: "TP4：撤 L_REQ/U_REQ 后等搬运车撤 BUSY、给 COMPT")]
    public int Tp4Timeout
    {
        get { return GetEcInt(nameof(Tp4Timeout)); }
        set { SetEcInt(nameof(Tp4Timeout), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "2000", description: "TP5：撤 READY 后等搬运车撤 VALID")]
    public int Tp5Timeout
    {
        get { return GetEcInt(nameof(Tp5Timeout)); }
        set { SetEcInt(nameof(Tp5Timeout), value); }
    }

    #endregion

    #region Alarm

    [Alarm("E84 交接超时", AlarmCategory.Timeout,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "E84 握手某一段（TP1–TP5）超时，本次交接中止，输出已全灭",
        Solution = "检查搬运车与端口的 E84 信号和光幕；确认载具实际位置后 Retry 或 Complete，再复位报警")]
    public string E84TimeoutAlarm = nameof(E84TimeoutAlarm);

    #endregion

    #region 状态

    private readonly object _gate = new();
    private readonly Stopwatch _stepWatch = new();
    private readonly List<E84Report> _reports = new();
    private E84Inputs _inputs;
    private E84Inputs _loggedInputs;
    private E84Outputs _outputs;
    private E84Outputs _written;
    private E84State _state = E84State.NotAvailable;
    private E84Timer? _timedOutTimer;

    /// <summary>
    /// 这次交接的方向：null = 没在交接；true = 送盒；false = 取盒。亮 L_REQ/U_REQ 时定下，收尾或放弃时清掉；
    /// 超时锁住期间保留，Complete 要按它核对载具位置。
    /// </summary>
    private bool? _isLoad;

    public E84State State
    {
        get { lock (_gate) { return _state; } }
    }

    public E84Inputs Inputs
    {
        get { lock (_gate) { return _inputs; } }
    }

    public E84Outputs Outputs
    {
        get { lock (_gate) { return _outputs; } }
    }

    public E84Timer? TimedOutTimer
    {
        get { lock (_gate) { return _timedOutTimer; } }
    }

    #endregion

    #region 打开与人工恢复

    public bool Open()
    {
        lock (_gate)
        {
            Clear();
            _reports.Clear();
            WriteOutputs(_outputs);
            _written = _outputs;
            return true;
        }
    }

    public void Retry()
    {
        lock (_gate)
        {
            LogHelper.Info(FullPath, "E84 Retry：放弃这次交接，重新等搬运车");
            if (IsHandoffStarted(_state) && _isLoad is { } isLoad)
            {
                _reports.Add(E84Report.Aborted(isLoad, "人工 Retry"));
            }

            Clear();
            Flush();
        }
    }

    public bool Complete(bool carrierPlaced)
    {
        lock (_gate)
        {
            if (_state != E84State.TimedOut || _isLoad is not { } isLoad)
            {
                LogHelper.Warn(FullPath, "E84 没有超时锁住的交接，Complete 不处理");
                return false;
            }

            if (isLoad != carrierPlaced)
            {
                LogHelper.Warn(FullPath, isLoad
                    ? "E84 Complete 被拒：载具不在位，送盒没有完成"
                    : "E84 Complete 被拒：载具还在，取盒没有完成");
                return false;
            }

            LogHelper.Info(FullPath, $"E84 人工确认{Direction(isLoad)}交接已完成");
            _reports.Add(E84Report.Completed(isLoad));
            Clear();
            Flush();
            return true;
        }
    }

    /// <summary>
    /// 输出全灭、清方向与超时锁存，回到不可交接（CTC ResetSignal）。
    /// </summary>
    private void Clear()
    {
        _outputs = default;
        _isLoad = null;
        _timedOutTimer = null;
        _state = E84State.NotAvailable;
        _stepWatch.Reset();
    }

    #endregion

    #region 推进

    public IReadOnlyList<E84Report> Step(E84Permit permit, bool carrierPlaced)
    {
        lock (_gate)
        {
            _inputs = ReadInputs();
            Advance(permit, carrierPlaced);
            Flush();

            if (_reports.Count == 0)
            {
                return Array.Empty<E84Report>();
            }

            var reports = _reports.ToArray();
            _reports.Clear();
            return reports;
        }
    }

    private void Advance(E84Permit permit, bool carrierPlaced)
    {
        // 闸门：E84 没开、端口不可交接、光幕被挡 → 输出全灭，进行中的交接按中止处理。
        // 超时锁存不因此解开（跟 CTC 一样），仍等人工恢复。
        string? closed = GateClosedReason(permit);
        if (closed is not null)
        {
            CloseGate(closed);
            return;
        }

        if (_state == E84State.TimedOut)
        {
            return;
        }

        _outputs = _outputs with { HoAvbl = true, Es = true };

        var i = _inputs;
        bool selected = i.Cs0 && i.Valid;

        // 送盒等载具放上，取盒等载具被取走。
        bool carrierDone = _isLoad is { } load && load == carrierPlaced;

        switch (_state)
        {
            case E84State.NotAvailable:
            case E84State.Available:
                // 搬运车 CS_0+VALID 选中本端口：等送盒且没载具就亮 L_REQ，等取盒且有载具就亮 U_REQ。
                if (selected && permit == E84Permit.ReadyToLoad && !carrierPlaced)
                {
                    Request(isLoad: true);
                }
                else if (selected && permit == E84Permit.ReadyToUnload && carrierPlaced)
                {
                    Request(isLoad: false);
                }
                else
                {
                    _state = E84State.Available;
                }

                break;

            case E84State.Requesting:
                if (!selected)
                {
                    // 还没开始交接搬运车就撤了：收回请求，回去等下一次。
                    LogHelper.Info(FullPath, $"E84 搬运车撤销选中，收回{Direction(_isLoad == true)}请求");
                    _outputs = _outputs with { LReq = false, UReq = false };
                    _isLoad = null;
                    Enter(E84State.Available);
                }
                else if (i.TrReq && !carrierDone)
                {
                    _outputs = _outputs with { Ready = true };
                    Enter(E84State.WaitBusy);
                    LogHelper.Info(FullPath, $"E84 {Direction(_isLoad == true)}交接开始");
                    _reports.Add(E84Report.Started(_isLoad == true));
                }

                break;

            case E84State.WaitBusy:
                if (i.Busy)
                {
                    Enter(E84State.Transferring);
                }

                break;

            case E84State.Transferring:
                if (carrierDone)
                {
                    _outputs = _outputs with { LReq = false, UReq = false };
                    Enter(E84State.WaitComplete);
                }

                break;

            case E84State.WaitComplete:
                if (i.Compt)
                {
                    _outputs = _outputs with { Ready = false };
                    Enter(E84State.Releasing);
                }

                break;

            case E84State.Releasing:
                if (!i.Valid && !i.TrReq && !i.Busy && !i.Compt && carrierDone)
                {
                    bool isLoad = _isLoad == true;
                    _isLoad = null;
                    Enter(E84State.Available);
                    LogHelper.Info(FullPath, $"E84 {Direction(isLoad)}交接完成");
                    _reports.Add(E84Report.Completed(isLoad));
                }

                break;
        }

        CheckTimeout();
    }

    private string? GateClosedReason(E84Permit permit)
    {
        if (!E84Enabled)
        {
            return "E84 未启用";
        }

        if (permit == E84Permit.NotAvailable)
        {
            return "端口不可交接（Manual / 下线 / Out Of Service）";
        }

        bool lightCurtainBlocked = LightCurtainReverse ? _inputs.LightCurtain : !_inputs.LightCurtain;
        if (DiLightCurtainIndex >= 0 && lightCurtainBlocked)
        {
            return "光幕被挡";
        }

        return null;
    }

    private void CloseGate(string reason)
    {
        _outputs = default;
        if (_state == E84State.TimedOut)
        {
            return;
        }

        if (IsHandoffStarted(_state) && _isLoad is { } isLoad)
        {
            LogHelper.Warn(FullPath, $"E84 {Direction(isLoad)}交接中止：{reason}");
            _reports.Add(E84Report.Aborted(isLoad, reason));
        }

        _isLoad = null;
        _state = E84State.NotAvailable;
        _stepWatch.Reset();
    }

    private void Request(bool isLoad)
    {
        _isLoad = isLoad;
        _outputs = isLoad ? _outputs with { LReq = true } : _outputs with { UReq = true };
        Enter(E84State.Requesting);
        LogHelper.Info(FullPath, $"E84 搬运车选中本端口，请求{Direction(isLoad)}");
    }

    /// <summary>
    /// 进下一步；带计时的步从这一刻起算超时。
    /// </summary>
    private void Enter(E84State state)
    {
        _state = state;
        _stepWatch.Restart();
    }

    /// <summary>
    /// 当前步超时了：撤 L_REQ/U_REQ/READY/HO_AVBL（ES 保持），锁住等人工恢复，报警并上报。
    /// </summary>
    private void CheckTimeout()
    {
        if (TimerOf(_state) is not { } timer || _stepWatch.ElapsedMilliseconds <= TimeoutOf(timer))
        {
            return;
        }

        bool isLoad = _isLoad == true;
        _outputs = _outputs with { LReq = false, UReq = false, Ready = false, HoAvbl = false };
        _timedOutTimer = timer;
        _state = E84State.TimedOut;
        _stepWatch.Reset();
        LogHelper.Warn(FullPath,
            $"E84 {Direction(isLoad)}交接 {timer} 超时（{TimeoutOf(timer)}ms），输出已撤，等人工 Retry 或 Complete");
        RaiseAlarm(E84TimeoutAlarm);
        _reports.Add(E84Report.TimedOut(isLoad, timer));
    }

    /// <summary>
    /// 每一步由哪段 TP 计时；不计时的步为 null。
    /// </summary>
    private static E84Timer? TimerOf(E84State state)
    {
        return state switch
        {
            E84State.Requesting => E84Timer.TP1,
            E84State.WaitBusy => E84Timer.TP2,
            E84State.Transferring => E84Timer.TP3,
            E84State.WaitComplete => E84Timer.TP4,
            E84State.Releasing => E84Timer.TP5,
            _ => null,
        };
    }

    private int TimeoutOf(E84Timer timer)
    {
        return timer switch
        {
            E84Timer.TP1 => Tp1Timeout,
            E84Timer.TP2 => Tp2Timeout,
            E84Timer.TP3 => Tp3Timeout,
            E84Timer.TP4 => Tp4Timeout,
            _ => Tp5Timeout,
        };
    }

    /// <summary>
    /// 已给出 READY、交接真正开始的步（这之后被打断要按中止上报）。
    /// </summary>
    private static bool IsHandoffStarted(E84State state)
    {
        return state is E84State.WaitBusy or E84State.Transferring or E84State.WaitComplete or E84State.Releasing;
    }

    /// <summary>
    /// 把这一拍的输出写出去（有变化才写），HO_AVBL 变了上报；信号有变化记一条日志（CTC RecordSignalChange）。
    /// </summary>
    private void Flush()
    {
        if (_inputs != _loggedInputs || _outputs != _written)
        {
            _loggedInputs = _inputs;
            LogHelper.Info(FullPath,
                $"E84 信号 VALID={Bit(_inputs.Valid)} CS_0={Bit(_inputs.Cs0)} TR_REQ={Bit(_inputs.TrReq)} " +
                $"BUSY={Bit(_inputs.Busy)} COMPT={Bit(_inputs.Compt)} | L_REQ={Bit(_outputs.LReq)} " +
                $"U_REQ={Bit(_outputs.UReq)} READY={Bit(_outputs.Ready)} HO_AVBL={Bit(_outputs.HoAvbl)} ES={Bit(_outputs.Es)}");
        }

        if (_outputs == _written)
        {
            return;
        }

        bool availabilityChanged = _outputs.HoAvbl != _written.HoAvbl;
        WriteOutputs(_outputs);
        _written = _outputs;
        if (availabilityChanged)
        {
            _reports.Add(E84Report.AvailabilityChanged(_outputs.HoAvbl));
        }
    }

    private static string Direction(bool isLoad)
    {
        return isLoad ? "送盒" : "取盒";
    }

    private static char Bit(bool value)
    {
        return value ? '1' : '0';
    }

    #endregion

    #region IO（还没接）

    /// <summary>
    /// 读一拍输入。IO 读写还没接，先按输入全 OFF 处理（搬运车不会被当成选中了本端口）；
    /// 接 IO 时按上面的 DI 索引读，没配的（-1）当 OFF。
    /// </summary>
    protected virtual E84Inputs ReadInputs()
    {
        return default;
    }

    /// <summary>
    /// 写一拍输出（有变化才调）。IO 读写还没接，先只记在 Outputs 上；
    /// 接 IO 时按上面的 DO 索引写，没配的（-1）跳过。
    /// </summary>
    protected virtual void WriteOutputs(E84Outputs outputs)
    {
    }

    #endregion
}
