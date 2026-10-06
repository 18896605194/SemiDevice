using System.Diagnostics;
using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 一趟搬运：把片从源站点的槽位搬到目标站点的槽位。
/// 手动传片和自动派单下的都是这个东西，执行完全一样，区别只在谁下的单（Origin）。
/// 这不是 CJ/PJ——那两层在上面（Job），带自己的状态机、可暂停、跨整盒片；这儿就是一次物理搬运，跑完即弃。
/// 由搬运管理的扫描线程一拍一拍推进（不挂在哪个模块上）。每一步等的是 IsSettled：模块落好状态、记完账才算这一步完。
/// 先抢目标站点再取片：目标准备不好就不取。没动手（还没落"交互中"标记）就失败或被撤时，把占着的环还回去；
/// 动了手再失败，片在哪说不准，环留在原地等人工确认（站点卡住正好挡住后续自动动作）。
/// 片已经在机械手手上（源是机械手）时只放片：抢目标站点 → 准备二 → 放片。
/// </summary>
public sealed class TransferRoutine : ModuleOperation<TransferStep>
{
    /// <summary>
    /// 站点准备第 1 步（准备一：粗准备，排气、预热、机构到位）。出错时步号作为错误参数报给界面，
    /// 界面按语言包写成"第 1 步"——不把中文阶段名当参数传，英文界面里才不会夹中文。
    /// </summary>
    private const int FirstPreparePhase = 1;

    /// <summary>站点准备第 2 步（准备二：最终准备，开门、放行），做完站点进 TransferReady。</summary>
    private const int SecondPreparePhase = 2;

    private readonly IRobot _robot;
    /// <summary>源站点；片已经在手上（只放片）时为 null。</summary>
    private readonly ITransferStation? _source;
    private readonly ITransferStation _target;
    private readonly int _sourceSlot;
    private readonly int _targetSlot;
    private readonly int _arm;
    private readonly int _stationWaitTimeout;

    /// <summary>源和目标是同一个站点：环只抢一次，取完不收尾接着放。</summary>
    private readonly bool _sameStation;

    /// <summary>当前在等的那一步操作（站点准备、取片、放片）；没在等为 null。</summary>
    private ModuleOperation? _current;

    /// <summary>这一站是从什么时候开始抢的；抢站点那两步判超时用，换站点时重置。</summary>
    private long _grabSince;

    /// <summary>占着源站点的环（准备过、还没收尾）。</summary>
    private bool _holdingSource;

    /// <summary>占着目标站点的环。</summary>
    private bool _holdingTarget;

    /// <summary>
    /// 这一趟是谁下的单。执行不看它，失败怎么收场看它。
    /// </summary>
    public TransferOrigin Origin { get; }

    /// <summary>搬片的机械手。</summary>
    public IRobot Robot => _robot;

    /// <summary>源站点；片已经在机械手手上（只放片）时为 null。</summary>
    public ITransferStation? Source => _source;

    /// <summary>目标站点。</summary>
    public ITransferStation Target => _target;

    /// <summary>源槽号（从 1 开始）。</summary>
    public int SourceSlot => _sourceSlot;

    /// <summary>目标槽号（从 1 开始）。</summary>
    public int TargetSlot => _targetSlot;

    /// <summary>用哪只手。</summary>
    public int Arm => _arm;

    /// <summary>
    /// 动过手了：落过"交互中"标记、发过取片（不管成没成）。之后再失败，片在哪说不准，要留着等人工确认；
    /// 没动手就失败，片还在源槽、环也还回去了。
    /// </summary>
    public bool MotionStarted { get; private set; }

    /// <summary>
    /// 机械手的取片或放片正在做：被中止时要给机械手发设备中止，光撤软件上的操作停不住手臂。
    /// </summary>
    public bool IsMoving => Step is TransferStep.WaitPick or TransferStep.WaitPlace;

    /// <summary>
    /// 取片做完了：片在机械手手上、账上记好了，还没放。记进结果（<see cref="TransferResult.Picked"/>），没搬成时分得清出错的是取片还是放片。
    /// 只放片的搬运（源是机械手）没有这一步。
    /// </summary>
    public bool HasPicked { get; private set; }

    public TransferRoutine(
        TransferOrigin origin,
        IRobot robot,
        ITransferStation? source,
        int sourceSlot,
        ITransferStation target,
        int targetSlot,
        int arm,
        int stationWaitTimeout)
        : base($"Transfer {source?.Name ?? robot.Name}.{sourceSlot:00}→{target.Name}.{targetSlot:00}",
            IsSameStation(source, target) ? TransferStep.PrepareSource : TransferStep.PrepareTarget)
    {
        Origin = origin;
        _robot = robot;
        _source = source;
        _sourceSlot = sourceSlot;
        _target = target;
        _targetSlot = targetSlot;
        _arm = arm;
        _stationWaitTimeout = stationWaitTimeout;
        _sameStation = IsSameStation(source, target);
        _grabSince = Stopwatch.GetTimestamp();
    }

    private static bool IsSameStation(ITransferStation? source, ITransferStation target)
    {
        return source is not null
            && (ReferenceEquals(source, target) || string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>取片那几步用的源站点；只放片的搬运走不到那几步（真走到了说明步骤乱了，抛出去由操作落 OperationFaulted）。</summary>
    private ITransferStation SourceStation => _source ?? throw new InvalidOperationException("只放片的搬运没有源站点");

    protected override void OnScan()
    {
        switch (Step)
        {
            // —— 先抢目标站点：放不下就不取 ——

            case TransferStep.PrepareTarget:
                if (GrabStation(_target, TransferStep.WaitPrepareTarget))
                {
                    _holdingTarget = true;
                }

                break;

            case TransferStep.WaitPrepareTarget:
                // 片已经在手上：不用取，直接去目标站点的准备二、放片
                if (WaitPrepare(_target, FirstPreparePhase, _source is null ? TransferStep.PrepareTarget2 : TransferStep.PrepareSource))
                {
                    // 换到源站点，抢站点的等待重新计时。
                    _grabSince = Stopwatch.GetTimestamp();
                }

                break;

            // —— 源站点：准备 → 取片 ——

            case TransferStep.PrepareSource:
                if (GrabStation(SourceStation, TransferStep.WaitPrepareSource))
                {
                    _holdingSource = true;
                }

                break;

            case TransferStep.WaitPrepareSource:
                WaitPrepare(SourceStation, FirstPreparePhase, TransferStep.PrepareSource2);
                break;

            case TransferStep.PrepareSource2:
                BeginPrepare(SourceStation, SourceStation.PrepareTransfer2(), SecondPreparePhase, TransferStep.WaitPrepareSource2);
                break;

            case TransferStep.WaitPrepareSource2:
                WaitPrepare(SourceStation, SecondPreparePhase, TransferStep.Pick);
                break;

            case TransferStep.Pick:
                BeginTransfer(SourceStation, _sourceSlot, pick: true, TransferStep.WaitPick);
                break;

            case TransferStep.WaitPick:
                WaitPicked();
                break;

            // —— 目标站点：准备二 → 放片 ——

            case TransferStep.PrepareTarget2:
                BeginPrepare(_target, _target.PrepareTransfer2(), SecondPreparePhase, TransferStep.WaitPrepareTarget2);
                break;

            case TransferStep.WaitPrepareTarget2:
                WaitPrepare(_target, SecondPreparePhase, TransferStep.Place);
                break;

            case TransferStep.Place:
                BeginTransfer(_target, _targetSlot, pick: false, TransferStep.WaitPlace);
                break;

            case TransferStep.WaitPlace:
                WaitPlaced();
                break;

            default:
                Abandon(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }

    /// <summary>
    /// 抢站点（发准备一）。站点不在锚点态——正被另一台机械手服务、或上一轮还没收尾完——
    /// 会返回 null，这不算失败：下一拍接着抢，等到超时才判负。抢到返回 true。
    /// 两台机械手抢同一个站点靠的就是这个：站点环在锁里校验当前态，只有一台能抢进去。
    /// </summary>
    private bool GrabStation(ITransferStation station, TransferStep next)
    {
        var operation = station.PrepareTransfer();
        if (operation is not null)
        {
            _current = operation;
            SetStep(next);
            return true;
        }

        if (Stopwatch.GetElapsedTime(_grabSince).TotalMilliseconds > _stationWaitTimeout)
        {
            Abandon(ErrorCodes.StationBusy,
                $"{station.Name} 等不到可服务（{_stationWaitTimeout}ms）",
                station.Name,
                _stationWaitTimeout.ToString(CultureInfo.InvariantCulture));
        }

        return false;
    }

    /// <summary>
    /// 发一步准备。进了环之后（准备二）被拒就是真故障：环是把锁，我们已经占住了，
    /// 这时候还被拒说明站点状态被人从旁边动过。
    /// </summary>
    private void BeginPrepare(ITransferStation station, ModuleOperation? operation, int phase, TransferStep next)
    {
        if (operation is null)
        {
            Abandon(ErrorCodes.StationPrepareRejected,
                $"{station.Name} 第 {phase} 步准备被拒",
                station.Name,
                phase.ToString(CultureInfo.InvariantCulture));
            return;
        }

        _current = operation;
        SetStep(next);
    }

    /// <summary>
    /// 等一步准备收尾完成；成功才走下一步，做完返回 true。
    /// </summary>
    private bool WaitPrepare(ITransferStation station, int phase, TransferStep next)
    {
        var operation = _current;
        if (operation is null || !operation.IsSettled)
        {
            return false;
        }

        _current = null;

        if (!operation.IsSuccess)
        {
            Abandon(ErrorCodes.StationPrepareFailed,
                $"{station.Name} 第 {phase} 步准备失败：{operation.Reason}",
                station.Name,
                phase.ToString(CultureInfo.InvariantCulture));
            return false;
        }

        SetStep(next);
        return true;
    }

    /// <summary>
    /// 落"交互中"标记并发起取/放片。标记必须先落：机械手要伸手进去了，这期间站点不能被别人动。
    /// 从落标记起就算动过手了；同站点换槽时取片已经落过标记，放片不再落。
    /// </summary>
    private void BeginTransfer(ITransferStation station, int slot, bool pick, TransferStep next)
    {
        bool marked = !pick && _sameStation;
        if (!marked)
        {
            MotionStarted = true;
            if (!station.Transferring())
            {
                Fail(ErrorCodes.TransferStepRejected,
                    $"{station.Name} 落不下 Transferring 标记",
                    station.Name,
                    "Transferring");
                return;
            }
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
    /// 等取片收尾完成（机械手记完账）。成功后源站点收尾回锚点态；同站点换槽不收尾，接着放。
    /// 失败不收尾——片可能半挂在手上，这时候关门会撞。站点就卡在 Transferring，挡住后续自动动作，逼人到现场确认。
    /// </summary>
    private void WaitPicked()
    {
        var operation = TakeSettled();
        if (operation is null)
        {
            return;
        }

        if (!operation.IsSuccess)
        {
            ReportMotionFailure(SourceStation, "Pick", operation);
            return;
        }

        HasPicked = true;
        if (_sameStation)
        {
            SetStep(TransferStep.Place);
            return;
        }

        SourceStation.TransferComplete();
        _holdingSource = false;
        SetStep(TransferStep.PrepareTarget2);
    }

    /// <summary>
    /// 等放片收尾完成。成功后目标站点收尾回锚点态，整趟结束。
    /// </summary>
    private void WaitPlaced()
    {
        var operation = TakeSettled();
        if (operation is null)
        {
            return;
        }

        if (!operation.IsSuccess)
        {
            ReportMotionFailure(_target, "Place", operation);
            return;
        }

        _target.TransferComplete();
        _holdingTarget = false;
        _holdingSource = false;
        Complete();
    }

    /// <summary>
    /// 当前在等的操作收尾完成了就交出来（清掉），还没完返回 null。
    /// </summary>
    private ModuleOperation? TakeSettled()
    {
        var operation = _current;
        if (operation is null || !operation.IsSettled)
        {
            return null;
        }

        _current = null;
        return operation;
    }

    private void ReportMotionFailure(ITransferStation station, string action, ModuleOperation operation)
    {
        LogHelper.Error(_robot.Name,
            $"{action} 失败，{station.Name} 停在 Transferring 等人工确认：{operation.Reason}");
        Fail(ErrorCodes.TransferFailed,
            $"{_robot.Name} {action} 失败：{operation.Reason}",
            _robot.Name,
            action);
    }

    /// <summary>
    /// 失败收场：还没动手就把占着的环还回去（片还在源槽，站点可以接着被服务），再落失败。
    /// </summary>
    private void Abandon(string code, string reason, params string[] args)
    {
        ReleaseRings();
        Fail(code, reason, args);
    }

    /// <summary>
    /// 被撤单（急停、人工取消、Job 中止）：在途的那一步操作跟着撤；没动手就把环还回去。
    /// 动过手的环不动——跟失败时一个道理，片在哪说不准，留着卡住等人工。
    /// </summary>
    protected override void OnAborted(string reason)
    {
        _current?.AbortByHost(reason);
        _current = null;
        ReleaseRings();
    }

    /// <summary>
    /// 还环：只在没动过手时还，站点自己决定能不能撤（只撤准备阶段的）。
    /// </summary>
    private void ReleaseRings()
    {
        if (MotionStarted)
        {
            return;
        }

        if (_holdingSource)
        {
            _source?.CancelTransfer();
            _holdingSource = false;
        }

        if (_holdingTarget && !_sameStation)
        {
            _target.CancelTransfer();
            _holdingTarget = false;
        }
    }
}
