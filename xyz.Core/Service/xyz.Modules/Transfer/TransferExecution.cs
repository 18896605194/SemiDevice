using System.Diagnostics;
using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 搬运执行的公共部分：抢站点、准备、换段、落交互标记并发取放、等结果、失败挂起、中止；一趟搬运和同腔交换都从这里派生
/// </summary>
public abstract class TransferExecution : ModuleOperation<TransferStep>
{
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

    private readonly int _stationWaitTimeout;

    /// <summary>
    /// 当前在等的那一步操作（站点准备、换段、取片、放片）；没在等为 null
    /// </summary>
    private ModuleOperation? _current;

    /// <summary>
    /// 这一站从什么时候开始抢的；抢站点判超时用，换站点时重置
    /// </summary>
    private long _grabSince;

    private volatile bool _abortRequested;
    private ModuleOperation? _abortOperation;
    private TransferClaims? _claims;

    /// <summary>
    /// 建执行：机械手、来源、等站点空出来的上限
    /// </summary>
    protected TransferExecution(string name, TransferStep initialStep, TransferOrigin origin, ITransferRobot robot, int stationWaitTimeout)
        : base(name, initialStep)
    {
        Origin = origin;
        Robot = robot;
        _stationWaitTimeout = stationWaitTimeout;
        _grabSince = Stopwatch.GetTimestamp();
    }

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

    /// <summary>
    /// 这一趟独占的资源清单（第一次用时按受理好的参数建，之后不变）
    /// </summary>
    public TransferClaims Claims
    {
        get
        {
            var claims = _claims;
            if (claims is null)
            {
                claims = CreateClaims();
                _claims = claims;
            }

            return claims;
        }
    }

    /// <summary>
    /// 动过手了：落过"交互中"标记、发过取放。之后再失败片在哪说不准，要留着等人工确认
    /// </summary>
    public bool MotionStarted { get; private set; }

    /// <summary>
    /// 动过手以后失败，或者站点占用没撤掉：资源保留到人工确认片位及站点状态
    /// </summary>
    public bool NeedsRecovery => IsTerminal && !IsSuccess && (MotionStarted || IsHoldingStation);

    /// <summary>
    /// 机械手的取片或放片正在做：被中止时要给机械手发设备中止
    /// </summary>
    public bool IsMoving => Step is TransferStep.WaitPick or TransferStep.WaitPlace;

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
    /// 当前设备动作和设备中止都收尾后，搬运管理才释放资源、唤醒等待方
    /// </summary>
    internal bool ReadyToFinish => IsTerminal && (_current is null || _current.IsSettled)
        && (_abortOperation is null || _abortOperation.IsSettled);

    /// <summary>
    /// 日志里这一趟的几片
    /// </summary>
    internal string WaferNames => string.Join("、", Wafers.Select(wafer => wafer.Name));

    /// <summary>
    /// 这一趟有没有这片
    /// </summary>
    public bool Involves(Guid waferId)
    {
        return Wafers.Any(wafer => wafer.Id == waferId);
    }

    /// <summary>
    /// 这一趟有没有属于这个 Job 的片
    /// </summary>
    public bool InvolvesOwner(string owner)
    {
        return Wafers.Any(wafer => string.Equals(wafer.Owner, owner, StringComparison.Ordinal));
    }

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

    /// <summary>
    /// 换站点了，抢站点的等待重新计时
    /// </summary>
    protected void RestartStationWait()
    {
        _grabSince = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// 抢站点（发准备一）：站点不在待命返回 null 不算失败，下一拍接着抢，等到超时才判负；抢到返回 true
    /// </summary>
    protected bool GrabStation(ITransferStation station, TransferContext context, TransferStep next)
    {
        var operation = station.PrepareTransfer(context);
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
    /// 发一步准备（准备二、换段准备）：已经占着站点还被拒，说明站点被人从旁边动过
    /// </summary>
    protected void BeginPrepare(ITransferStation station, ModuleOperation? operation, int phase, TransferStep next)
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
    /// 等一步准备收尾完成；成功才走下一步，做完返回 true
    /// </summary>
    protected bool WaitPrepare(ITransferStation station, int phase, TransferStep next)
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
    /// 先问机械手能不能做，再落"交互中"标记、发取片或放片；从落标记起就算动过手了
    /// </summary>
    protected void BeginMotion(ITransferStation station, TransferContext context, int arm, int slot, bool pick, TransferStep next)
    {
        string action = "Place";
        HandleResult? refused;
        if (pick)
        {
            action = "Pick";
            refused = Robot.CheckPick(arm, station.Name, slot);
        }
        else
        {
            refused = Robot.CheckPlace(arm, station.Name, slot);
        }

        if (refused is not null)
        {
            Abandon(refused.ErrorMessage, $"{Robot.Name} {action} 被拒：{refused.ErrorMessage}", [.. refused.Args]);
            return;
        }

        MotionStarted = true;
        if (!station.Transferring(context))
        {
            Fail(ErrorCodes.TransferStepRejected,
                $"{station.Name} 落不下 Transferring 标记",
                station.Name,
                "Transferring");
            return;
        }

        HandleResult<ModuleOperation> started;
        if (pick)
        {
            started = Robot.Pick(arm, station.Name, slot);
        }
        else
        {
            started = Robot.Place(arm, station.Name, slot);
        }

        var operation = started.Result;
        if (!started.IsSuccess || operation is null)
        {
            Fail(started.ErrorMessage, $"{Robot.Name} {action} 被拒：{started.ErrorMessage}", [.. started.Args]);
            return;
        }

        _current = operation;
        SetStep(next);
    }

    /// <summary>
    /// 当前在等的操作收尾完成了就交出来（清掉），还没完返回 null
    /// </summary>
    protected ModuleOperation? TakeSettled()
    {
        var operation = _current;
        if (operation is null || !operation.IsSettled)
        {
            return null;
        }

        _current = null;
        return operation;
    }

    /// <summary>
    /// 取片或放片没做成：站点停在 Transferring 等人工确认；账没记上的原样带出账的错误码
    /// </summary>
    protected void ReportMotionFailure(ITransferStation station, string action, ModuleOperation operation)
    {
        LogHelper.Error(Robot.Name,
            $"{action} 失败，{station.Name} 停在 Transferring 等人工确认：{operation.Reason}");
        if (operation.Code == ErrorCodes.RobotLedgerMoveFailed)
        {
            Fail(operation.Code, operation.Reason, [.. operation.ErrorArgs]);
            return;
        }

        Fail(ErrorCodes.TransferFailed,
            $"{Robot.Name} {action} 失败：{operation.Reason}",
            Robot.Name,
            action);
    }

    /// <summary>
    /// 失败收场：没动过手就先撤站点，撤不掉保留占用，再落原始失败
    /// </summary>
    protected void Abandon(string code, string reason, params string[] args)
    {
        ReleaseStations();
        Fail(code, reason, args);
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

            LogHelper.Error(Robot.Name, $"{station.Name} 撤销搬运准备被拒，保留本轮占用");
        }
        catch (Exception exception)
        {
            LogHelper.Error(Robot.Name, $"{station.Name} 撤销搬运准备异常，保留本轮占用：{exception.Message}");
        }

        return false;
    }

    /// <summary>
    /// 记这一片做到哪一步
    /// </summary>
    protected static void MarkProgress(TransferWafer wafer, TransferWaferProgress progress)
    {
        wafer.Progress = progress;
    }

    /// <summary>
    /// 被中止（急停、人工取消、Job 中止）：在途的设备动作跟着中止；没动过手才撤站点
    /// </summary>
    protected override void OnAborted(string reason)
    {
        if (IsMoving)
        {
            _abortOperation = Robot.Abort();
        }

        _current?.AbortByHost(reason);
        ReleaseStations();
    }
}
