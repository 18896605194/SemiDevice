using System.Diagnostics;
using xyz.Common.Log;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 一趟搬运：把片从源站点的槽位搬到目标站点的槽位。
/// 手动传片和自动派单下的都是这个东西，执行完全一样，区别只在谁下的单（Origin）。
/// 这不是 CJ/PJ——那两层在上面，带自己的状态机、可暂停、跨整盒片；这儿就是一次物理搬运，跑完即弃。
/// </summary>
public sealed class TransferRoutine : ModuleOperation<TransferStep>
{
    private readonly IRobot _robot;
    private readonly ITransferStation _source;
    private readonly ITransferStation _target;
    private readonly int _sourceSlot;
    private readonly int _targetSlot;
    private readonly int _arm;
    private readonly int _stationWaitTimeout;

    /// <summary>当前在等的那一步操作（站点准备、取片、放片）；没在等为 null。</summary>
    private ModuleOperation? _current;

    /// <summary>这一站是从什么时候开始抢的；抢站点那两步判超时用，换站点时重置。</summary>
    private long _grabSince;

    /// <summary>
    /// 这一趟是谁下的单。执行不看它，失败怎么收场看它。
    /// </summary>
    public TransferOrigin Origin { get; }

    public TransferRoutine(
        TransferOrigin origin,
        IRobot robot,
        ITransferStation source,
        int sourceSlot,
        ITransferStation target,
        int targetSlot,
        int arm,
        int stationWaitTimeout)
        : base($"Transfer {source.Name}.{sourceSlot:00}→{target.Name}.{targetSlot:00}", TransferStep.PrepareSource)
    {
        Origin = origin;
        _robot = robot;
        _source = source;
        _sourceSlot = sourceSlot;
        _target = target;
        _targetSlot = targetSlot;
        _arm = arm;
        _stationWaitTimeout = stationWaitTimeout;
        _grabSince = Stopwatch.GetTimestamp();
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            // —— 源站点：准备 → 取片 ——

            case TransferStep.PrepareSource:
                GrabStation(_source, TransferStep.WaitPrepareSource);
                break;

            case TransferStep.WaitPrepareSource:
                WaitPrepare(_source, "准备一", TransferStep.PrepareSource2);
                break;

            case TransferStep.PrepareSource2:
                BeginPrepare(_source, _source.PrepareTransfer2(), "准备二", TransferStep.WaitPrepareSource2);
                break;

            case TransferStep.WaitPrepareSource2:
                WaitPrepare(_source, "准备二", TransferStep.Pick);
                break;

            case TransferStep.Pick:
                BeginTransfer(_source, _sourceSlot, pick: true, TransferStep.WaitPick);
                break;

            case TransferStep.WaitPick:
                WaitTransfer(_source, "Pick", TransferStep.PrepareTarget);
                break;

            // —— 目标站点：准备 → 放片 ——

            case TransferStep.PrepareTarget:
                GrabStation(_target, TransferStep.WaitPrepareTarget);
                break;

            case TransferStep.WaitPrepareTarget:
                WaitPrepare(_target, "准备一", TransferStep.PrepareTarget2);
                break;

            case TransferStep.PrepareTarget2:
                BeginPrepare(_target, _target.PrepareTransfer2(), "准备二", TransferStep.WaitPrepareTarget2);
                break;

            case TransferStep.WaitPrepareTarget2:
                WaitPrepare(_target, "准备二", TransferStep.Place);
                break;

            case TransferStep.Place:
                BeginTransfer(_target, _targetSlot, pick: false, TransferStep.WaitPlace);
                break;

            case TransferStep.WaitPlace:
                WaitTransfer(_target, "Place", next: null);
                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }

    /// <summary>
    /// 抢站点（发准备一）。站点不在锚点态——正被另一台机械手服务、或上一轮还没收尾完——
    /// 会返回 null，这不算失败：下一拍接着抢，等到超时才判负。
    /// 两台机械手抢同一个站点靠的就是这个：站点环在锁里校验当前态，只有一台能抢进去。
    /// </summary>
    private void GrabStation(ITransferStation station, TransferStep next)
    {
        var operation = station.PrepareTransfer();
        if (operation is not null)
        {
            _current = operation;
            SetStep(next);
            return;
        }

        if (Stopwatch.GetElapsedTime(_grabSince).TotalMilliseconds > _stationWaitTimeout)
        {
            Fail(ErrorCodes.StationBusy,
                $"{station.Name} 等不到可服务（{_stationWaitTimeout}ms）",
                station.Name,
                _stationWaitTimeout.ToString());
        }
    }

    /// <summary>
    /// 发一步准备。进了环之后（准备二及以后）被拒就是真故障：环是把锁，我们已经占住了，
    /// 这时候还被拒说明站点状态被人从旁边动过。
    /// </summary>
    private void BeginPrepare(ITransferStation station, ModuleOperation? operation, string phase, TransferStep next)
    {
        if (operation is null)
        {
            Fail(ErrorCodes.StationPrepareRejected, $"{station.Name} {phase}被拒", station.Name, phase);
            return;
        }

        _current = operation;
        SetStep(next);
    }

    /// <summary>
    /// 等一步准备做完。
    /// </summary>
    private void WaitPrepare(ITransferStation station, string phase, TransferStep next)
    {
        var operation = _current;
        if (operation is null || !operation.IsTerminal)
        {
            return;
        }

        _current = null;

        if (!operation.IsSuccess)
        {
            Fail(ErrorCodes.StationPrepareFailed,
                $"{station.Name} {phase}失败：{operation.Reason}",
                station.Name,
                phase);
            return;
        }

        SetStep(next);
    }

    /// <summary>
    /// 落"交互中"标记并发起取/放片。标记必须先落：机械手要伸手进去了，这期间站点不能被别人动。
    /// </summary>
    private void BeginTransfer(ITransferStation station, int slot, bool pick, TransferStep next)
    {
        if (!station.Transferring())
        {
            Fail(ErrorCodes.TransferStepRejected,
                $"{station.Name} 落不下 Transferring 标记",
                station.Name,
                "Transferring");
            return;
        }

        string action = pick ? "Pick" : "Place";
        var operation = pick
            ? _robot.Pick(_arm, station.Name, slot)
            : _robot.Place(_arm, station.Name, slot);

        if (operation is null)
        {
            Fail(ErrorCodes.TransferRejected, $"{_robot.Name} {action} 被拒", _robot.Name, action);
            return;
        }

        _current = operation;
        SetStep(next);
    }

    /// <summary>
    /// 等取/放片终结。成功才让站点收尾回锚点态；next 为 null 表示放完了，整趟结束。
    /// 失败不收尾——片可能半挂在手上，这时候关门会撞。站点就卡在 Transferring，
    /// 正好挡住后续所有自动动作，逼人到现场确认片在哪再复位。
    /// </summary>
    private void WaitTransfer(ITransferStation station, string action, TransferStep? next)
    {
        var operation = _current;
        if (operation is null || !operation.IsTerminal)
        {
            return;
        }

        _current = null;

        if (!operation.IsSuccess)
        {
            LogHelper.Error(_robot.Name,
                $"{action} 失败，{station.Name} 停在 Transferring 等人工确认：{operation.Reason}");
            Fail(ErrorCodes.TransferFailed,
                $"{_robot.Name} {action} 失败：{operation.Reason}",
                _robot.Name,
                action);
            return;
        }

        station.TransferComplete();

        if (next is null)
        {
            Complete();
            return;
        }

        // 换到目标站点，抢站点的等待重新计时。
        _grabSince = Stopwatch.GetTimestamp();
        SetStep(next.Value);
    }

    /// <summary>
    /// 被撤单（急停、人工取消）：在途的那一步操作跟着撤。
    /// 站点上的环标记不动——跟失败时一个道理，片在哪说不准，留着卡住等人工。
    /// </summary>
    protected override void OnAborted(string reason)
    {
        _current?.AbortByHost(reason);
        _current = null;
    }
}
