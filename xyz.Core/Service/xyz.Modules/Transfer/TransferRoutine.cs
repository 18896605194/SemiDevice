using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 一趟搬运的动作
/// </summary>
public sealed class TransferRoutine : TransferExecution
{
    private readonly ITransferStation? _source;

    private readonly ITransferStation? _target;
    private readonly int _sourceSlot;
    private readonly int _targetSlot;
    private readonly int _arm;

    /// <summary>源和目标是同一个站点：环只抢一次，取完不收尾，换段准备以后接着放。</summary>
    private readonly bool _sameStation;

    private readonly TransferContext? _sourceContext;
    private readonly TransferContext? _targetContext;

    /// <summary>
    /// 这一趟搬的那一片和它的进度
    /// </summary>
    private readonly TransferWafer _wafer;

    private readonly IReadOnlyList<TransferWafer> _wafers;

    /// <summary>占着源站点的环（准备过、还没收尾）。</summary>
    private bool _holdingSource;

    /// <summary>占着目标站点的环。</summary>
    private bool _holdingTarget;

    /// <summary>所属 PJ；手动传片为空。</summary>
    public string? Owner
    {
        get { return _wafer.Owner; }
        internal init { _wafer.Owner = value; }
    }

    /// <summary>这一趟搬的晶圆，用于资源占用和人工确认后放锁。</summary>
    public Guid WaferId
    {
        get { return _wafer.Id; }
        internal init { _wafer.Id = value; }
    }

    internal string WaferName
    {
        get { return _wafer.Name; }
        init { _wafer.Name = value; }
    }

    public string SourceName => _source?.Name ?? Robot.Name;

    /// <summary>目标站点名；只取片时是机械手名（目标槽号就是手指号）。</summary>
    public string TargetName => _target?.Name ?? Robot.Name;

    /// <summary>源站点；片已经在机械手手上（只放片）时为 null。</summary>
    public ITransferStation? Source => _source;

    /// <summary>目标站点；只取片时为 null。</summary>
    public ITransferStation? Target => _target;

    /// <summary>源槽号（从 1 开始）。</summary>
    public int SourceSlot => _sourceSlot;

    /// <summary>目标槽号（从 1 开始）。</summary>
    public int TargetSlot => _targetSlot;

    /// <summary>用哪只手。</summary>
    public int Arm => _arm;

    /// <summary>
    /// 取片做完了：片在机械手手上、账上记好了。调度据此分清出错的是取片还是放片。
    /// 只放片的搬运（源是机械手）没有这一步。
    /// </summary>
    public bool HasPicked => _wafer.Progress != TransferWaferProgress.None;

    public override IReadOnlyList<TransferWafer> Wafers => _wafers;

    protected override bool IsHoldingStation => _holdingSource || _holdingTarget;

    public TransferRoutine(
        TransferOrigin origin,
        ITransferRobot robot,
        ITransferStation? source,
        int sourceSlot,
        ITransferStation? target,
        int targetSlot,
        int arm,
        int stationWaitTimeout)
        : base($"Transfer {source?.Name ?? robot.Name}.{sourceSlot:00}→{target?.Name ?? robot.Name}.{targetSlot:00}",
            FirstStep(source, target), origin, robot, stationWaitTimeout)
    {
        if (source is null && target is null)
        {
            throw new ArgumentException("源和目标不能都是机械手：至少有一头是站点");
        }

        _source = source;
        _sourceSlot = sourceSlot;
        _target = target;
        _targetSlot = targetSlot;
        _arm = arm;
        _sameStation = IsSameStation(source, target);
        _wafer = new TransferWafer(SourceName, sourceSlot, TargetName, targetSlot, arm, target is null);
        _wafers = [_wafer];

        if (_sameStation)
        {
            _sourceContext = TransferContext.ForPickThenPlace(robot.Name, arm, sourceSlot, arm, targetSlot);
            _targetContext = _sourceContext;
            return;
        }

        if (source is not null)
        {
            _sourceContext = TransferContext.ForPick(robot.Name, arm, sourceSlot);
        }

        if (target is not null)
        {
            _targetContext = TransferContext.ForPlace(robot.Name, arm, targetSlot);
        }
    }

    /// <summary>
    /// 从哪一步开始：同一个站点换槽、只取片先抢源站点，其余先抢目标站点（放不下就不取）
    /// </summary>
    private static TransferStep FirstStep(ITransferStation? source, ITransferStation? target)
    {
        if (IsSameStation(source, target) || target is null)
        {
            return TransferStep.PrepareSource;
        }

        return TransferStep.PrepareTarget;
    }

    private static bool IsSameStation(ITransferStation? source, ITransferStation? target)
    {
        return source is not null && target is not null
            && (ReferenceEquals(source, target) || string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase));
    }

    protected override TransferClaims CreateClaims()
    {
        var stations = new List<string>();
        if (_source is not null)
        {
            stations.Add(_source.Name);
        }

        if (_target is not null)
        {
            stations.Add(_target.Name);
        }

        return new TransferClaims(
            [TransferManager.SlotKey(SourceName, _sourceSlot), TransferManager.SlotKey(TargetName, _targetSlot)],
            [TransferManager.ArmKey(Robot.Name, _arm)],
            [WaferId],
            stations);
    }

    /// <summary>取片那几步用的源站点；只放片的搬运走不到那几步（真走到了说明步骤乱了，抛出去由操作落 OperationFaulted）。</summary>
    private ITransferStation SourceStation => _source ?? throw new InvalidOperationException("只放片的搬运没有源站点");

    /// <summary>放片那几步用的目标站点；只取片的搬运走不到那几步。</summary>
    private ITransferStation TargetStation => _target ?? throw new InvalidOperationException("只取片的搬运没有目标站点");

    private TransferContext SourceContext => _sourceContext ?? throw new InvalidOperationException("只放片的搬运没有来源站点上下文");

    private TransferContext TargetContext => _targetContext ?? throw new InvalidOperationException("只取片的搬运没有目标站点上下文");

    protected override void OnScan()
    {
        switch (Step)
        {
            // —— 先抢目标站点：放不下就不取 ——

            case TransferStep.PrepareTarget:
                if (GrabStation(TargetStation, TargetContext, TransferStep.WaitPrepareTarget))
                {
                    _holdingTarget = true;
                }

                break;

            case TransferStep.WaitPrepareTarget:
                WaitTargetFirstPrepare();
                break;

            // —— 源站点：准备 → 取片 ——

            case TransferStep.PrepareSource:
                if (GrabStation(SourceStation, SourceContext, TransferStep.WaitPrepareSource))
                {
                    _holdingSource = true;
                }

                break;

            case TransferStep.WaitPrepareSource:
                WaitPrepare(SourceStation, FirstPreparePhase, TransferStep.PrepareSource2);
                break;

            case TransferStep.PrepareSource2:
                BeginPrepare(SourceStation, SourceStation.PrepareTransfer2(SourceContext), SecondPreparePhase, TransferStep.WaitPrepareSource2);
                break;

            case TransferStep.WaitPrepareSource2:
                WaitPrepare(SourceStation, SecondPreparePhase, TransferStep.Pick);
                break;

            case TransferStep.Pick:
                BeginMotion(SourceStation, SourceContext, _arm, _sourceSlot, true, TransferStep.WaitPick);
                break;

            case TransferStep.WaitPick:
                WaitPicked();
                break;

            // —— 同站换槽：取完不收尾，换段准备后接着放 ——

            case TransferStep.PrepareNextLeg:
                BeginPrepare(SourceStation, SourceStation.PrepareNextLeg(SourceContext), NextLegPhase, TransferStep.WaitPrepareNextLeg);
                break;

            case TransferStep.WaitPrepareNextLeg:
                WaitPrepare(SourceStation, NextLegPhase, TransferStep.Place);
                break;

            // —— 目标站点：准备二 → 放片 ——

            case TransferStep.PrepareTarget2:
                BeginPrepare(TargetStation, TargetStation.PrepareTransfer2(TargetContext), SecondPreparePhase, TransferStep.WaitPrepareTarget2);
                break;

            case TransferStep.WaitPrepareTarget2:
                WaitPrepare(TargetStation, SecondPreparePhase, TransferStep.Place);
                break;

            case TransferStep.Place:
                BeginMotion(TargetStation, TargetContext, _arm, _targetSlot, false, TransferStep.WaitPlace);
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
    /// 目标站点准备一做完：片已经在手上就直接去目标准备二，否则换到源站点去抢（抢站点的等待重新计时）
    /// </summary>
    private void WaitTargetFirstPrepare()
    {
        var next = TransferStep.PrepareSource;
        if (_source is null)
        {
            next = TransferStep.PrepareTarget2;
        }

        if (WaitPrepare(TargetStation, FirstPreparePhase, next))
        {
            RestartStationWait();
        }
    }

    /// <summary>
    /// 等取片收尾完成（机械手记完账）。成功后源站点收尾回待命态；同站点换槽不收尾，去换段准备。
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

        _wafer.Progress = TransferWaferProgress.Picked;
        if (_sameStation)
        {
            SetStep(TransferStep.PrepareNextLeg);
            return;
        }

        if (!SourceStation.TransferComplete(SourceContext))
        {
            Fail(ErrorCodes.TransferStepRejected,
                $"{SourceStation.Name} 取片后的收尾被拒",
                SourceStation.Name,
                "TransferComplete");
            return;
        }

        _holdingSource = false;

        // 只取片：片留在手上，这一趟到这儿就做完了
        if (_target is null)
        {
            Complete();
            return;
        }

        SetStep(TransferStep.PrepareTarget2);
    }

    /// <summary>
    /// 等放片收尾完成。成功后目标站点收尾回待命态，整趟结束。
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
            ReportMotionFailure(TargetStation, "Place", operation);
            return;
        }

        _wafer.Progress = TransferWaferProgress.Placed;
        if (!TargetStation.TransferComplete(TargetContext))
        {
            Fail(ErrorCodes.TransferStepRejected,
                $"{TargetStation.Name} 放片后的收尾被拒",
                TargetStation.Name,
                "TransferComplete");
            return;
        }

        _holdingTarget = false;
        _holdingSource = false;
        Complete();
    }

    /// <summary>
    /// 还环：只在没动过手时撤销，站点接受撤销后才清占用；一站拒绝或异常不影响另一站撤销。
    /// </summary>
    protected override void ReleaseStations()
    {
        if (MotionStarted)
        {
            return;
        }

        if (_holdingSource && _source is not null && TryCancelStation(_source, SourceContext))
        {
            _holdingSource = false;
        }

        if (_holdingTarget && !_sameStation && _target is not null && TryCancelStation(_target, TargetContext))
        {
            _holdingTarget = false;
        }
    }
}
