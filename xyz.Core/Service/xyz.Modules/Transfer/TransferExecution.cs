using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 搬运执行的公共部分：抢站点、准备、换段、落交互标记并发取放、分段取放的交接、失败挂起、中止；一趟搬运和同腔交换都从这里派生，
/// 派生类在构造里按情况把步骤排好（放不下就不取、同站换槽、只取片、只放片）。搬运管理的扫描线程推进
/// </summary>
public abstract class TransferExecution : ModuleRoutine
{
    #region 字段与构造

    /// <summary>
    /// 抢站点时等它空出来的上限（ms）
    /// </summary>
    private readonly int _stationWaitTimeout;

    /// <summary>
    /// 取片、放片（分段的伸臂、交接、缩臂）是第几步（从 0 数）：被中止时停在这几步要给机械手发设备中止
    /// </summary>
    private readonly HashSet<int> _motionSteps = [];

    /// <summary>
    /// 给机械手发的设备中止；没发为 null
    /// </summary>
    private ModuleRoutine? _robotAbort;

    /// <summary>
    /// 建执行：日志记在谁名下（搬运管理）、动作名、机械手、来源、等站点空出来的上限
    /// </summary>
    protected TransferExecution(ILogSource owner, string name, TransferOrigin origin, ITransferRobot robot, int stationWaitTimeout)
        : base(owner, name)
    {
        Origin = origin;
        Robot = robot;
        _stationWaitTimeout = stationWaitTimeout;
    }

    #endregion

    #region 这一趟的信息（搬运管理、调度读）

    /// <summary>
    /// 谁发起的（自动、手动、恢复）
    /// </summary>
    public TransferOrigin Origin { get; }

    /// <summary>
    /// 搬片的机械手
    /// </summary>
    public ITransferRobot Robot { get; }

    /// <summary>
    /// 这一趟涉及的片和各自做到哪一步
    /// </summary>
    public abstract IReadOnlyList<TransferWafer> Wafers { get; }

    private TransferClaims? _claims;

    /// <summary>
    /// 这一趟独占的资源清单（第一次用时按受理好的参数建，之后不变）
    /// </summary>
    public TransferClaims Claims => _claims ??= CreateClaims();

    /// <summary>
    /// 动过手了：落过"交互中"标记、发过取放。之后再失败片在哪说不准，要留着等人工确认
    /// </summary>
    public bool MotionStarted { get; private set; }

    /// <summary>
    /// 动过手以后失败，或者站点占用没撤掉：资源保留到人工确认片位及站点状态
    /// </summary>
    public bool NeedsRecovery => IsTerminal && !IsSuccess && (MotionStarted || IsHoldingStation);

    /// <summary>
    /// 日志里这一趟的几片
    /// </summary>
    internal string WaferNames => string.Join("、", Wafers.Select(wafer => wafer.Name));

    /// <summary>
    /// 这一趟有没有属于这个 Job 的片
    /// </summary>
    public bool InvolvesOwner(string owner)
    {
        return Wafers.Any(wafer => string.Equals(wafer.Owner, owner, StringComparison.Ordinal));
    }

    /// <summary>
    /// 这一趟还有这个 Job 的片没走完：只取不放的取完算走完，其余放完算走完
    /// </summary>
    public bool HasPendingWork(string owner)
    {
        foreach (var wafer in Wafers)
        {
            if (!string.Equals(wafer.Owner, owner, StringComparison.Ordinal))
            {
                continue;
            }

            var done = TransferWaferProgress.Placed;
            if (wafer.EndsOnArm)
            {
                done = TransferWaferProgress.Picked;
            }

            if (wafer.Progress != done)
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region 子类实现

    /// <summary>
    /// 还占着站点的环（准备过、还没收尾或撤掉）
    /// </summary>
    protected abstract bool IsHoldingStation { get; }

    /// <summary>
    /// 按受理好的参数建资源清单
    /// </summary>
    protected abstract TransferClaims CreateClaims();

    /// <summary>
    /// 没动过手时撤掉本趟占着的站点；动过手的不撤，留着等人工
    /// </summary>
    protected abstract void ReleaseStations();

    #endregion

    #region 排步骤：抢站点、准备

    /// <summary>
    /// 准备一（抢站点），报错参数里的第几步
    /// </summary>
    protected const int FirstPreparePhase = 1;

    /// <summary>
    /// 准备二，报错参数里的第几步
    /// </summary>
    protected const int SecondPreparePhase = 2;

    /// <summary>
    /// 换段准备，报错参数里的第几步
    /// </summary>
    protected const int NextLegPhase = 3;

    /// <summary>
    /// 加一步：抢站点（发准备一），抢到了 grabbed 记下占着它，再等准备一做完；站点不在待命就接着抢，等到上限才判负
    /// </summary>
    protected void AddGrabStep(ITransferStation station, TransferContext context, Action grabbed)
    {
        Add(new StationGrabStep($"抢 {station.Name}", station, context, _stationWaitTimeout, grabbed));
    }

    /// <summary>
    /// 加一步：发一步准备（准备二、换段准备）、等它做完。已经占着站点还被拒，说明站点被人从旁边动过
    /// </summary>
    protected void AddPrepareStep(string name, ITransferStation station, Func<ModuleRoutine?> prepare, int phase)
    {
        string number = phase.ToString(CultureInfo.InvariantCulture);
        Start(name, () => Prepared(station, prepare(), number),
            (step, routine) => step.Fail(ErrorCodes.StationPrepareFailed, $"{station.Name} 第 {number} 步准备失败：{routine.Reason}", station.Name, number));
    }

    /// <summary>
    /// 站点给的准备动作：给了 null 是被拒
    /// </summary>
    private static HandleResult<ModuleRoutine> Prepared(ITransferStation station, ModuleRoutine? routine, string phase)
    {
        if (routine is null)
        {
            return HandleResult<ModuleRoutine>.Fail(ErrorCodes.StationPrepareRejected, station.Name, phase);
        }

        return HandleResult<ModuleRoutine>.Success(routine);
    }

    #endregion

    #region 排步骤：取放

    /// <summary>
    /// 加一下取片或放片：站点没配中途交接就是一步；配了拆成 伸臂 → 站点交接 → 缩臂（机械手要有分段能力 <see cref="ITransferRobotStep"/>，
    /// 没有的走到这一步就失败）。从落"交互中"标记起就算动过手了；没做成站点停在 Transferring 等人工确认
    /// </summary>
    protected void AddMotionSteps(string name, ITransferStation station, TransferContext context, int arm, int slot, bool pick)
    {
        bool handoff = station.HandoffOnPlace;
        if (pick)
        {
            handoff = station.HandoffOnPick;
        }

        if (!handoff)
        {
            Start(name, () => BeginMotion(station, context, arm, slot, pick, null), (step, routine) => ReportMotionFailure(step, station, pick, routine));
            MarkMotion();
            return;
        }

        var stepRobot = Robot as ITransferRobotStep;
        if (stepRobot is null)
        {
            Do(name, () => HandleResult.Fail(ErrorCodes.RobotHandoffUnsupported, Robot.Name));
            return;
        }

        Start($"{name}：伸臂", () => BeginMotion(station, context, arm, slot, pick, stepRobot), (step, routine) => ReportMotionFailure(step, station, pick, routine));
        MarkMotion();
        Start($"{name}：{station.Name} 交接", () => BeginHandoff(station, context, pick),
            (step, routine) => StopAtHandoff(step, ErrorCodes.StationHandoffFailed, $"{station.Name} 中途交接没做成：{routine.Reason}", station.Name));
        MarkMotion();
        Start($"{name}：缩臂", () => BeginRetract(stepRobot, station, arm, slot, pick), (step, routine) => ReportMotionFailure(step, station, pick, routine));
        MarkMotion();
    }

    /// <summary>
    /// 刚加的这一步是取放（被中止时要给机械手发设备中止）
    /// </summary>
    private void MarkMotion()
    {
        _motionSteps.Add(StepCount - 1);
    }

    /// <summary>
    /// 先问机械手能不能做，再落"交互中"标记、发取片或放片（分段的发伸臂）
    /// </summary>
    private HandleResult<ModuleRoutine> BeginMotion(ITransferStation station, TransferContext context, int arm, int slot, bool pick,
        ITransferRobotStep? stepRobot)
    {
        HandleResult? refused;
        if (pick)
        {
            refused = Robot.CheckPick(arm, station.Name, slot);
        }
        else
        {
            refused = Robot.CheckPlace(arm, station.Name, slot);
        }

        if (refused is not null)
        {
            return HandleResult<ModuleRoutine>.Fail(refused.ErrorMessage, [.. refused.Args]);
        }

        MotionStarted = true;
        if (!station.Transferring(context))
        {
            return HandleResult<ModuleRoutine>.Fail(ErrorCodes.TransferStepRejected, station.Name, "Transferring");
        }

        if (stepRobot is not null && pick)
        {
            return stepRobot.ExtendPick(arm, station.Name, slot);
        }

        if (stepRobot is not null)
        {
            return stepRobot.ExtendPlace(arm, station.Name, slot);
        }

        if (pick)
        {
            return Robot.Pick(arm, station.Name, slot);
        }

        return Robot.Place(arm, station.Name, slot);
    }

    /// <summary>
    /// 伸臂做完：叫站点交接；站点不收，机械手就地停下
    /// </summary>
    private HandleResult<ModuleRoutine> BeginHandoff(ITransferStation station, TransferContext context, bool pick)
    {
        ModuleRoutine? handoff;
        if (pick)
        {
            handoff = station.HandoffPick(context);
        }
        else
        {
            handoff = station.HandoffPlace(context);
        }

        if (handoff is null)
        {
            StopRobot($"{station.Name} 中途交接被拒", station.Name);
            return HandleResult<ModuleRoutine>.Fail(ErrorCodes.StationHandoffRejected, station.Name);
        }

        return HandleResult<ModuleRoutine>.Success(handoff);
    }

    /// <summary>
    /// 站点交接做完：叫机械手缩臂；缩臂被拒按机械手给的原因失败（手还伸着，等人工）
    /// </summary>
    private HandleResult<ModuleRoutine> BeginRetract(ITransferRobotStep stepRobot, ITransferStation station, int arm, int slot, bool pick)
    {
        HandleResult<ModuleRoutine> retract;
        if (pick)
        {
            retract = stepRobot.RetractPick(arm, station.Name, slot);
        }
        else
        {
            retract = stepRobot.RetractPlace(arm, station.Name, slot);
        }

        if (!retract.IsSuccess)
        {
            LogRobotError($"{station.Name} 交接做完了，缩臂被拒：{retract.ErrorMessage}，手还伸在站里等人工确认");
        }

        return retract;
    }

    /// <summary>
    /// 取片或放片（伸臂、缩臂）没做成：站点停在 Transferring 等人工确认；账没记上的原样带出账的错误码
    /// </summary>
    private void ReportMotionFailure(RoutineStep step, ITransferStation station, bool pick, ModuleRoutine routine)
    {
        string action = "Place";
        if (pick)
        {
            action = "Pick";
        }

        LogRobotError($"{action} 失败，{station.Name} 停在 Transferring 等人工确认：{routine.Reason}");
        if (routine.Code == ErrorCodes.RobotLedgerMoveFailed)
        {
            step.Fail(routine.Code, routine.Reason, [.. routine.ErrorArgs]);
            return;
        }

        step.Fail(ErrorCodes.TransferFailed, $"{Robot.Name} {action} 失败：{routine.Reason}", Robot.Name, action);
    }

    /// <summary>
    /// 交接没做成：机械手伸着停在站里，就地中止它（不缩臂，片可能交了一半），整趟失败等人工确认片位
    /// </summary>
    private void StopAtHandoff(RoutineStep step, string code, string reason, string station)
    {
        StopRobot(reason, station);
        step.Fail(code, reason, station);
    }

    /// <summary>
    /// 机械手伸在站里时就地停下
    /// </summary>
    private void StopRobot(string reason, string station)
    {
        LogRobotError($"{reason}，机械手停在 {station} 里等人工确认");
        _robotAbort = Robot.Abort().Result;
    }

    #endregion

    #region 失败收场、中止

    private volatile bool _abortRequested;

    /// <summary>
    /// 失败收场：没动过手就撤站点（撤不掉保留占用）；动过手的留着等人工
    /// </summary>
    protected override void OnFailed()
    {
        ReleaseStations();
    }

    /// <summary>
    /// 撤一个站点的本轮准备；站点拒绝或异常就保留占用
    /// </summary>
    protected bool TryCancelStation(ITransferStation station, TransferContext context)
    {
        try
        {
            if (station.CancelTransfer(context))
            {
                return true;
            }

            LogRobotError($"{station.Name} 撤销搬运准备被拒，保留本轮占用");
        }
        catch (Exception exception)
        {
            LogRobotError($"{station.Name} 撤销搬运准备异常，保留本轮占用：{exception.Message}");
        }

        return false;
    }

    internal bool AbortRequested => _abortRequested;

    internal string AbortReason { get; private set; } = string.Empty;

    /// <summary>
    /// 请求中止，由搬运管理的扫描线程执行
    /// </summary>
    internal void RequestAbort(string reason)
    {
        AbortReason = reason;
        _abortRequested = true;
    }

    /// <summary>
    /// 机械手的取片或放片（分段的伸臂、交接、缩臂）正在做：被中止时要给机械手发设备中止
    /// </summary>
    public bool IsMoving => _motionSteps.Contains(StepNumber - 1);

    /// <summary>
    /// 被中止（急停、人工取消、Job 中止）：机械手在取放就先给它发设备中止，没动过手才撤站点；在等的站点准备、交接随后由这一步跟着中止
    /// </summary>
    protected override void OnAborted(string reason)
    {
        if (IsMoving)
        {
            _robotAbort = Robot.Abort().Result;
        }

        ReleaseStations();
    }

    /// <summary>
    /// 停下来了，这一步手上的站点准备、取放、交接和给机械手的设备中止都收完尾，搬运管理才释放资源、唤醒等待方
    /// </summary>
    internal bool ReadyToFinish
    {
        get
        {
            var robotAbort = _robotAbort;
            return IsTerminal && IsChildSettled && (robotAbort is null || robotAbort.IsSettled);
        }
    }

    #endregion

    /// <summary>
    /// 机械手出的事记在机械手名下（机械手都是组件）；不是组件的（测试用的假机械手）记在搬运管理名下、带上机械手名
    /// </summary>
    private void LogRobotError(string message)
    {
        if (Robot is ILogSource source)
        {
            LogHelper.Error(source, message);
            return;
        }

        LogHelper.Error(LogOwner, $"{Robot.Name}：{message}");
    }
}
