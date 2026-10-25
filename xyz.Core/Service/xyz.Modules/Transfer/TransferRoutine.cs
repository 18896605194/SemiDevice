using xyz.Common.Log;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 一趟搬运的动作：先抢目标站点（放不下就不取），再到源站点 准备 → 取片 → 收尾，回目标站点 准备二 → 放片 → 收尾。
/// 同一个站点换槽：环只抢一次，取完不收尾，换段准备以后接着放；只取片（目标是机械手）取完就算做完；只放片（源是机械手）没有取片那几步
/// </summary>
public sealed class TransferRoutine : TransferExecution
{
    private readonly ITransferStation? _source;

    private readonly ITransferStation? _target;
    private readonly int _sourceSlot;
    private readonly int _targetSlot;
    private readonly int _arm;

    /// <summary>
    /// 源和目标是同一个站点：环只抢一次，取完不收尾，换段准备以后接着放
    /// </summary>
    private readonly bool _sameStation;

    private readonly TransferContext? _sourceContext;
    private readonly TransferContext? _targetContext;

    /// <summary>
    /// 这一趟搬的那一片和它的进度
    /// </summary>
    private readonly TransferWafer _wafer;

    private readonly IReadOnlyList<TransferWafer> _wafers;

    /// <summary>
    /// 占着源站点的环（准备过、还没收尾）
    /// </summary>
    private bool _holdingSource;

    /// <summary>
    /// 占着目标站点的环
    /// </summary>
    private bool _holdingTarget;

    /// <summary>
    /// 所属 PJ；手动传片为空
    /// </summary>
    public string? Owner
    {
        get { return _wafer.Owner; }
        internal init { _wafer.Owner = value; }
    }

    /// <summary>
    /// 这一趟搬的晶圆，用于资源占用和人工确认后放锁
    /// </summary>
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

    /// <summary>
    /// 目标站点名；只取片时是机械手名（目标槽号就是手指号）
    /// </summary>
    public string TargetName => _target?.Name ?? Robot.Name;

    /// <summary>
    /// 源站点；片已经在机械手手上（只放片）时为 null
    /// </summary>
    public ITransferStation? Source => _source;

    /// <summary>
    /// 目标站点；只取片时为 null
    /// </summary>
    public ITransferStation? Target => _target;

    /// <summary>
    /// 源槽号（从 1 开始）
    /// </summary>
    public int SourceSlot => _sourceSlot;

    /// <summary>
    /// 目标槽号（从 1 开始）
    /// </summary>
    public int TargetSlot => _targetSlot;

    /// <summary>
    /// 用哪只手
    /// </summary>
    public int Arm => _arm;

    /// <summary>
    /// 取片做完了：片在机械手手上、账上记好了。调度据此分清出错的是取片还是放片。
    /// 只放片的搬运（源是机械手）没有这一步
    /// </summary>
    public bool HasPicked => _wafer.Progress != TransferWaferProgress.None;

    public override IReadOnlyList<TransferWafer> Wafers => _wafers;

    protected override bool IsHoldingStation => _holdingSource || _holdingTarget;

    /// <summary>
    /// 建一趟搬运，按源、目标的情况把步骤排好；owner 是日志记在谁名下（搬运管理）
    /// </summary>
    public TransferRoutine(
        ILogSource owner,
        TransferOrigin origin,
        ITransferRobot robot,
        ITransferStation? source,
        int sourceSlot,
        ITransferStation? target,
        int targetSlot,
        int arm,
        int stationWaitTimeout)
        : base(owner, $"Transfer {source?.Name ?? robot.Name}.{sourceSlot:00}→{target?.Name ?? robot.Name}.{targetSlot:00}",
            origin, robot, stationWaitTimeout)
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
        }
        else
        {
            if (source is not null)
            {
                _sourceContext = TransferContext.ForPick(robot.Name, arm, sourceSlot);
            }

            if (target is not null)
            {
                _targetContext = TransferContext.ForPlace(robot.Name, arm, targetSlot);
            }
        }

        AddSteps(source, target);
    }

    /// <summary>
    /// 排步骤：不是同站换槽、也不是只取片的，先抢目标站点（放不下就不取）
    /// </summary>
    private void AddSteps(ITransferStation? source, ITransferStation? target)
    {
        if (target is not null && !_sameStation)
        {
            AddGrabStep(target, TargetContext, () => _holdingTarget = true);
        }

        if (source is not null)
        {
            var sourceContext = SourceContext;
            AddGrabStep(source, sourceContext, () => _holdingSource = true);
            AddPrepareStep($"{source.Name} 准备二", source, () => source.PrepareTransfer2(sourceContext), SecondPreparePhase);
            AddMotionSteps("取片", source, sourceContext, _arm, _sourceSlot, true);
            Do("取完收尾", FinishPick);
        }

        if (target is null)
        {
            return;
        }

        var targetContext = TargetContext;
        if (_sameStation)
        {
            AddPrepareStep($"{target.Name} 换段准备", target, () => target.PrepareNextLeg(targetContext), NextLegPhase);
        }
        else
        {
            AddPrepareStep($"{target.Name} 准备二", target, () => target.PrepareTransfer2(targetContext), SecondPreparePhase);
        }

        AddMotionSteps("放片", target, targetContext, _arm, _targetSlot, false);
        Do("放完收尾", FinishPlace);
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

    private TransferContext SourceContext => _sourceContext ?? throw new InvalidOperationException("只放片的搬运没有来源站点上下文");

    private TransferContext TargetContext => _targetContext ?? throw new InvalidOperationException("只取片的搬运没有目标站点上下文");

    /// <summary>
    /// 取片做完（机械手记完账）：源站点收尾回待命态；同站点换槽不收尾，接着换段准备。
    /// 取片失败不会走到这儿——片可能半挂在手上，这时候关门会撞，站点就卡在 Transferring，挡住后续自动动作，逼人到现场确认
    /// </summary>
    private HandleResult FinishPick()
    {
        _wafer.Progress = TransferWaferProgress.Picked;
        if (_sameStation)
        {
            return HandleResult.Success();
        }

        var source = _source;
        if (source is not null)
        {
            if (!source.TransferComplete(SourceContext))
            {
                return HandleResult.Fail(ErrorCodes.TransferStepRejected, source.Name, "TransferComplete");
            }

            _holdingSource = false;
        }

        return HandleResult.Success();
    }

    /// <summary>
    /// 放片做完：目标站点收尾回待命态，整趟结束
    /// </summary>
    private HandleResult FinishPlace()
    {
        _wafer.Progress = TransferWaferProgress.Placed;
        var target = _target;
        if (target is not null && !target.TransferComplete(TargetContext))
        {
            return HandleResult.Fail(ErrorCodes.TransferStepRejected, target.Name, "TransferComplete");
        }

        _holdingTarget = false;
        _holdingSource = false;
        return HandleResult.Success();
    }

    /// <summary>
    /// 还环：只在没动过手时撤销，站点接受撤销后才清占用；一站拒绝或异常不影响另一站撤销
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
