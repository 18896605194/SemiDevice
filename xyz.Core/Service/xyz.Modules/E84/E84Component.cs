using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Components;
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
        @default: "60000", description: "TP4：撤 L_REQ/U_REQ 后等搬运车撤 BUSY")]
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
        Solution = "检查搬运车与端口的 E84 信号和光幕；确认载具实际位置后 Retry 或 Complete")]
    public string E84TimeoutAlarm = nameof(E84TimeoutAlarm);

    #endregion

    #region 状态

    private readonly object _gate = new();
    private readonly Dictionary<E84Timer, Stopwatch> _timers = new();
    private IE84Host? _host;
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

    /// <summary>
    /// 这次交接是否已经开始（READY 已给出、已上报 HandoffStarted）。
    /// </summary>
    private bool _started;

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

    #region 挂接与人工恢复

    public void Attach(IE84Host host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_gate)
        {
            _host = host;
            Clear();
            WriteOutputs(_outputs);
            _written = _outputs;
        }
    }

    public void Retry()
    {
        lock (_gate)
        {
            if (_host is not { } host)
            {
                return;
            }

            LogHelper.Info(FullPath, "E84 Retry：放弃这次交接，重新等搬运车");
            if (_timedOutTimer is null && _started && _isLoad is { } isLoad)
            {
                host.HandoffAborted(isLoad, "人工 Retry");
            }

            Clear();
            AlarmComponent.Current?.Clear(this, E84TimeoutAlarm);
            Flush(host);
        }
    }

    public bool Complete()
    {
        lock (_gate)
        {
            if (_host is not { } host || _timedOutTimer is null || _isLoad is not { } isLoad)
            {
                LogHelper.Warn(FullPath, "E84 没有超时锁住的交接，Complete 不处理");
                return false;
            }

            if (isLoad != host.IsCarrierPlaced)
            {
                LogHelper.Warn(FullPath, isLoad
                    ? "E84 Complete 被拒：载具不在位，送盒没有完成"
                    : "E84 Complete 被拒：载具还在，取盒没有完成");
                return false;
            }

            LogHelper.Info(FullPath, $"E84 人工确认{Direction(isLoad)}交接已完成");
            host.HandoffCompleted(isLoad);
            Clear();
            AlarmComponent.Current?.Clear(this, E84TimeoutAlarm);
            Flush(host);
            return true;
        }
    }

    /// <summary>
    /// 输出全灭、计时全停、清方向与超时锁存（CTC ResetSignal）。
    /// </summary>
    private void Clear()
    {
        _outputs = default;
        _timers.Clear();
        _isLoad = null;
        _started = false;
        _timedOutTimer = null;
        _state = E84State.NotAvailable;
    }

    #endregion

    #region 扫描（CTC E84Passiver.OnTimer）

    protected override void OnScan()
    {
        base.OnScan();

        lock (_gate)
        {
            if (_host is not { } host)
            {
                return;
            }

            _inputs = ReadInputs();
            Step(host);
            Flush(host);
        }
    }

    private void Step(IE84Host host)
    {
        // 闸门：E84 没开、不是 Auto、Out Of Service、光幕被挡 → 输出全灭，计时全停。
        // 超时锁存不因此解开（跟 CTC 一样），仍等人工恢复。
        string? blocked = BlockedReason(host);
        if (blocked is not null)
        {
            if (_timedOutTimer is null)
            {
                if (_started && _isLoad is { } isLoad)
                {
                    LogHelper.Warn(FullPath, $"E84 {Direction(isLoad)}交接中止：{blocked}");
                    host.HandoffAborted(isLoad, blocked);
                }

                _isLoad = null;
                _started = false;
                _state = E84State.NotAvailable;
            }

            _outputs = default;
            _timers.Clear();
            return;
        }

        if (_timedOutTimer is not null)
        {
            return;
        }

        _outputs = _outputs with { HoAvbl = true, Es = true };

        if (_isLoad is null)
        {
            TryRequest(host);
        }

        if (_isLoad is { } direction)
        {
            StepHandoff(host, direction);
        }

        CheckTimers(host);

        if (_timedOutTimer is null)
        {
            _state = _isLoad is null ? E84State.Available
                : !_started ? E84State.Requesting
                : _isLoad == true ? E84State.Loading
                : E84State.Unloading;
        }
    }

    private string? BlockedReason(IE84Host host)
    {
        if (!E84Enabled)
        {
            return "E84 未启用";
        }

        if (!host.IsAutoAccessMode)
        {
            return "端口不是 Auto";
        }

        if (host.TransferState == LoadPortTransferState.OutOfService)
        {
            return "端口 Out Of Service";
        }

        bool lightCurtainBlocked = LightCurtainReverse ? _inputs.LightCurtain : !_inputs.LightCurtain;
        if (DiLightCurtainIndex >= 0 && lightCurtainBlocked)
        {
            return "光幕被挡";
        }

        return null;
    }

    /// <summary>
    /// 没在交接时：搬运车 CS_0+VALID 选中本端口，端口等送盒且没载具就亮 L_REQ，等取盒且有载具就亮 U_REQ。
    /// </summary>
    private void TryRequest(IE84Host host)
    {
        if (!_inputs.Cs0 || !_inputs.Valid)
        {
            return;
        }

        bool placed = host.IsCarrierPlaced;
        switch (host.TransferState)
        {
            case LoadPortTransferState.ReadyToLoad when !placed:
                _outputs = _outputs with { LReq = true };
                _isLoad = true;
                break;

            case LoadPortTransferState.ReadyToUnload when placed:
                _outputs = _outputs with { UReq = true };
                _isLoad = false;
                break;

            default:
                return;
        }

        LogHelper.Info(FullPath, $"E84 搬运车选中本端口，请求{Direction(_isLoad == true)}");
    }

    /// <summary>
    /// 交接的一拍（CTC 的 Load/Unload 时序，送盒和取盒对称）：
    /// 请求段——等 TR_REQ（TP1），来了给 READY，算交接开始；
    /// 交接段——等 BUSY（TP2）→ 等载具放上/取走（TP3），到了撤 L_REQ/U_REQ → 等 BUSY 撤掉（TP4）
    /// → COMPT 来了撤 READY → 等 VALID 等信号全撤（TP5），算交接完成。
    /// </summary>
    private void StepHandoff(IE84Host host, bool isLoad)
    {
        var i = _inputs;
        bool selected = i.Cs0 && i.Valid;

        // 送盒等载具放上，取盒等载具被取走。
        bool carrierDone = isLoad == host.IsCarrierPlaced;

        if (!_started)
        {
            if (!selected)
            {
                // 还没开始交接搬运车就撤了：收回请求，回去等下一次。
                LogHelper.Info(FullPath, $"E84 搬运车撤销选中，收回{Direction(isLoad)}请求");
                _outputs = _outputs with { LReq = false, UReq = false };
                _timers.Remove(E84Timer.TP1);
                _isLoad = null;
                return;
            }

            if (!i.TrReq)
            {
                StartTimer(E84Timer.TP1);
                return;
            }

            if (!carrierDone)
            {
                _timers.Remove(E84Timer.TP1);
                _outputs = _outputs with { Ready = true };
                _started = true;
                LogHelper.Info(FullPath, $"E84 {Direction(isLoad)}交接开始");
                host.HandoffStarted(isLoad);
            }

            return;
        }

        bool request = isLoad ? _outputs.LReq : _outputs.UReq;

        if (selected && request && i.TrReq && _outputs.Ready && !i.Busy)
        {
            StartTimer(E84Timer.TP2);
        }

        if (selected && request && i.TrReq && _outputs.Ready && i.Busy)
        {
            _timers.Remove(E84Timer.TP2);
            if (!carrierDone)
            {
                StartTimer(E84Timer.TP3);
            }
            else
            {
                _timers.Remove(E84Timer.TP3);
                _outputs = isLoad ? _outputs with { LReq = false } : _outputs with { UReq = false };
                request = false;
            }
        }

        if (selected && !request && i.TrReq && _outputs.Ready && i.Busy)
        {
            StartTimer(E84Timer.TP4);
        }

        if (selected && !request && _outputs.Ready && !i.Busy)
        {
            _timers.Remove(E84Timer.TP4);
        }

        if (selected && !request && _outputs.Ready && i.Compt)
        {
            _outputs = _outputs with { Ready = false };
            _timers.Remove(E84Timer.TP4);
            StartTimer(E84Timer.TP5);
        }

        if (!i.Valid && !i.TrReq && !i.Busy && !i.Compt
            && !_outputs.LReq && !_outputs.UReq && !_outputs.Ready && carrierDone)
        {
            _timers.Remove(E84Timer.TP5);
            _isLoad = null;
            _started = false;
            LogHelper.Info(FullPath, $"E84 {Direction(isLoad)}交接完成");
            host.HandoffCompleted(isLoad);
        }
    }

    /// <summary>
    /// 哪段计时超了：撤 L_REQ/U_REQ/READY/HO_AVBL（ES 保持），锁住等人工恢复，报警并上报。
    /// </summary>
    private void CheckTimers(IE84Host host)
    {
        E84Timer? expired = null;
        foreach (var (timer, watch) in _timers)
        {
            if (watch.ElapsedMilliseconds > TimeoutOf(timer) && (expired is null || timer < expired))
            {
                expired = timer;
            }
        }

        if (expired is not { } timedOut)
        {
            return;
        }

        bool isLoad = _isLoad == true;
        _outputs = _outputs with { LReq = false, UReq = false, Ready = false, HoAvbl = false };
        _timers.Clear();
        _timedOutTimer = timedOut;
        _state = E84State.TimedOut;
        LogHelper.Warn(FullPath,
            $"E84 {Direction(isLoad)}交接 {timedOut} 超时（{TimeoutOf(timedOut)}ms），输出已撤，等人工 Retry 或 Complete");
        AlarmComponent.Current?.Raise(this, E84TimeoutAlarm);
        host.HandoffTimedOut(isLoad, timedOut);
    }

    private void StartTimer(E84Timer timer)
    {
        if (!_timers.ContainsKey(timer))
        {
            _timers[timer] = Stopwatch.StartNew();
        }
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
    /// 把这一拍的输出写出去（有变化才写），HO_AVBL 变了报给端口；信号有变化记一条日志（CTC RecordSignalChange）。
    /// </summary>
    private void Flush(IE84Host host)
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
            host.AvailabilityChanged(_outputs.HoAvbl);
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
