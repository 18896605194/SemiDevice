using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 调度引擎（sc.xml Job 节点下的 Scheduler 子节点，跟着 Job 的扫描线程）：从任务表拿任务、执行——每片看当前任务，
/// 站内任务交给站点自己做，取片连同它后面的放片交给搬运管理（目标空着、占住了才取），派不出去的下一拍再看；
/// 执行着的自己记着，做完了把结果记回任务表（完成、出错、退回等着做）。只看 Job 给每一行的许可，不认识暂停、停止。
/// 默认策略：先起站内任务，再走机内的片（先腾地方），最后投新片；站点组按 sc.xml 先后挑第一个能放的。
/// 不认机型名字：机型要别的策略，写个子类重写这里的方法，sc.xml 里把 Type 换掉。
/// </summary>
[Component(description: "调度引擎：从任务表拿任务执行（站内任务交给站点、取放交给搬运管理），做完记回任务表")]
public class SchedulerComponent : ComponentBase
{
    /// <summary>这一次派单用的搬运管理、晶圆账、锁快照（每次 <see cref="Dispatch"/> 开头取，只在 Job 的扫描线程上用）。</summary>
    private TransferManager? _transfers;
    private WaferManagerComponent? _ledger;
    private TransferView _locks = TransferView.Empty;

    /// <summary>交给搬运管理还没结束的取放（取片、放片一趟）。</summary>
    private readonly List<Move> _moves = [];

    /// <summary>交给站点还没做完的站内任务。</summary>
    private readonly List<Work> _works = [];

    /// <summary>一趟取放：哪一行的取片（只放片时为 null）、放片，等搬运管理的结果。</summary>
    private sealed record Move(TaskRow Row, WaferTask? Pick, WaferTask Place, Task<TransferResult> Completion);

    /// <summary>一个站内任务：等站点的操作做完（收完尾、记完账）。</summary>
    private sealed record Work(TaskRow Row, WaferTask Task, ModuleOperation Operation);

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "0", max: "100", @default: "0",
        description: "机内最多同时几片（投新片前查）；0 = 不限，靠目标站点空不空自然限住")]
    public int MaxWafersInMachine
    {
        get { return GetEcInt(nameof(MaxWafersInMachine)); }
        set { SetEcInt(nameof(MaxWafersInMachine), value); }
    }

    #endregion

    /// <summary>
    /// 派这一拍的任务。rows 是有许可的行，按优先级排好（CJ 队列先后、PJ 在 CJ 里的先后、PJ 里的投片顺序）。Manual 下 Job 组件不调。
    /// </summary>
    public virtual void Dispatch(IReadOnlyList<TaskRow> rows, BaseTaskComponent tasks)
    {
        _transfers = TransferManager.Current;
        _ledger = WaferManagerComponent.Current;
        if (_transfers is null || _ledger is null)
        {
            return;
        }

        _locks = _transfers.GetView();
        var claimedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedRobots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 先起站内任务：片已经在站点上了
        foreach (var row in rows)
        {
            var task = NextTask(row, TaskPermission.Advance);
            if (task is not null && !task.IsRobotTask)
            {
                StartStationTask(row, task, tasks);
            }
        }

        // 再走机内的片：做完的片占着站点，不挪走后面的片进不来
        foreach (var row in rows)
        {
            var task = NextTask(row, TaskPermission.Advance);
            if (task is not null && task.IsRobotTask && !row.IsWaiting)
            {
                PlanMove(row, task, tasks, claimedSlots, claimedRobots);
            }
        }

        // 最后投新片
        int limit = MaxWafersInMachine;
        int inMachine = rows.Count(row => row.IsInMachine);
        foreach (var row in rows)
        {
            var task = NextTask(row, TaskPermission.Feed);
            if (task is null || !row.IsWaiting || (limit > 0 && inMachine >= limit))
            {
                continue;
            }

            if (PlanMove(row, task, tasks, claimedSlots, claimedRobots))
            {
                inMachine++;
            }
        }
    }

    /// <summary>
    /// 收这一拍做完的（Job 每拍先调这里，再核对片位、派新任务）：站内操作收完尾的——做成了完成，PJ 中止时被打断的记未执行，别的没做成记出错；
    /// 搬运单结束了的看 <see cref="Finish"/>。
    /// </summary>
    public virtual void Collect(BaseTaskComponent tasks)
    {
        foreach (var work in _works.ToList())
        {
            var operation = work.Operation;
            if (!operation.IsSettled)
            {
                continue;
            }

            _works.Remove(work);
            if (operation.IsSuccess)
            {
                tasks.Done(work.Row, work.Task);
            }
            else if (work.Row.IsAborting && operation.State == OperationState.Aborted)
            {
                tasks.Cancel(work.Row, work.Task);
            }
            else
            {
                tasks.Fail(work.Row, work.Task, operation.Code, operation.ErrorArgs);
            }
        }

        foreach (var move in _moves.ToList())
        {
            if (move.Completion.IsCompleted)
            {
                _moves.Remove(move);
                Finish(move, move.Completion.Result, tasks);
            }
        }
    }

    /// <summary>
    /// 一趟取放结束：做完了取放都完成（记下用的哪只手）；没碰到片就失败（等不到站点、被撤），都退回等着做、下一拍重新派；
    /// 碰过片才失败，片在哪说不准，正在做的那一格记出错（取片没做完是取片，做完了是放片），停住等人处理。
    /// </summary>
    private static void Finish(Move move, TransferResult result, BaseTaskComponent tasks)
    {
        var row = move.Row;
        var pick = move.Pick;
        var place = move.Place;
        if (result.IsSuccess)
        {
            if (pick is not null)
            {
                tasks.Done(row, pick, result.Arm);
            }

            tasks.Done(row, place, result.Arm);
            return;
        }

        if (!result.NeedsRecovery)
        {
            if (pick is not null)
            {
                tasks.Reset(pick);
            }

            tasks.Reset(place);
            return;
        }

        if (pick is not null && !result.Picked)
        {
            tasks.Fail(row, pick, result.Code, result.Args);
            tasks.Reset(place);
            return;
        }

        if (pick is not null)
        {
            tasks.Done(row, pick, result.Arm);
        }

        tasks.Fail(row, place, result.Code, result.Args);
    }

    /// <summary>这一行轮到的、等着做的任务（有这个许可才算）；不该派返回 null。</summary>
    protected static WaferTask? NextTask(TaskRow row, TaskPermission needed)
    {
        var current = row.Current;
        if (current is null || current.State != WaferTaskState.Waiting || (row.Permission & needed) == 0)
        {
            return null;
        }

        return current;
    }

    /// <summary>
    /// 起站内任务：片要在站点上（上一个放片放到的地方），站点自己先查一遍能不能起（不动设备），起不了下一拍再看。
    /// </summary>
    protected virtual void StartStationTask(TaskRow row, WaferTask task, BaseTaskComponent tasks)
    {
        var location = row.ExpectedLocation;
        var station = location.IsArm ? null : Station(location.Module);
        if (station is null)
        {
            return;
        }

        var request = new StationTaskRequest
        {
            Kind = task.Kind,
            Owner = row.Owner,
            WaferId = row.WaferId,
            Slot = location.Slot,
            Step = task.Step,
            RecipeName = task.RecipeName,
            Recipe = task.Recipe,
        };
        if (!station.CheckTask(request).IsSuccess)
        {
            return;
        }

        var operation = station.StartTask(request);
        if (operation is not null)
        {
            tasks.Start(row, task, location.Module, location.Slot);
            _works.Add(new Work(row, task, operation));
        }
    }

    /// <summary>
    /// 排一趟搬运：取片连同它后面的放片（片在站点上），或只放片（取片做完了、片在机械手手上）。
    /// 源站点要能服务；目标要空着、没被锁、这一拍没被别的片挑走（回片槽是建 Job 时定好的）；要有机械手接得了。排上返回 true。
    /// </summary>
    protected virtual bool PlanMove(TaskRow row, WaferTask task, BaseTaskComponent tasks, ISet<string> claimedSlots, ISet<string> claimedRobots)
    {
        var source = row.ExpectedLocation;
        var pick = task.Kind == StationTaskAction.Pick ? task : null;
        var place = pick is null ? task : row.NextPlace(pick);

        // 取片要片在站点上、只放片要片在机械手手上；对不上说明片被人挪过，等任务组件核对片位记出错
        if (place is null || place.Kind != StationTaskAction.Place || source.IsArm == (pick is not null))
        {
            return false;
        }

        if (!source.IsArm && !IsStationReady(source.Module))
        {
            return false;
        }

        if (place.FixedSlot > 0 && place.Stations.Count > 0)
        {
            // 回片：回片槽是建 Job 时定好的
            string port = place.Stations[0];
            int slot = place.FixedSlot;
            return IsStationReady(port) && IsSlotFree(port, slot) && !claimedSlots.Contains(TransferManager.SlotKey(port, slot))
                && Submit(row, pick, place, source, port, slot, tasks, claimedSlots, claimedRobots);
        }

        foreach (string station in OrderTargets(row, place))
        {
            if (!IsStationReady(station))
            {
                continue;
            }

            int slot = FreeSlot(station, claimedSlots);
            if (slot > 0 && Submit(row, pick, place, source, station, slot, tasks, claimedSlots, claimedRobots))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 站点组里按什么先后挑：默认 sc.xml 的先后（流程配方里勾的先后），挑第一个能放的。机型要轮着用、挑最先空出来的就重写。
    /// </summary>
    protected virtual IEnumerable<string> OrderTargets(TaskRow row, WaferTask place)
    {
        return place.Stations;
    }

    #region 设备查询（每次派单开头取的锁快照）

    /// <summary>按名字找站点（搬运管理的模块表）；不是站点为 null。</summary>
    protected ITransferStation? Station(string name)
    {
        var transfers = _transfers;
        return transfers is not null && transfers.TryGetStation(name, out var station) ? station : null;
    }

    /// <summary>
    /// 站点现在能不能服务机械手：装了、启用、在待命、没在做别的动作；腔体这类可选的站点还要在线（参与自动调度），LoadPort 是 Job 自己的载具，不看在线。
    /// </summary>
    protected virtual bool IsStationReady(string name)
    {
        var station = Station(name);
        if (station is not BaseModule module || !module.IsEnabled || !station.CanPrepare)
        {
            return false;
        }

        if (module is not BaseLoadPortModule && module.Mode != ModuleMode.Online)
        {
            return false;
        }

        var current = module.CurrentOperation;
        return current is null || current.IsTerminal;
    }

    /// <summary>这一槽账上空着、也没被搬运单锁着。</summary>
    protected bool IsSlotFree(string station, int slot)
    {
        return _ledger is not null && _ledger.Get(station, slot) is null && !_locks.IsSlotLocked(station, slot);
    }

    /// <summary>站点上第一个空着、没被锁、这一拍没被挑走的槽；没有返回 0。</summary>
    protected int FreeSlot(string station, ISet<string> claimedSlots)
    {
        int count = Station(station)?.SlotCount ?? 0;
        for (int slot = 1; slot <= count; slot++)
        {
            if (IsSlotFree(station, slot) && !claimedSlots.Contains(TransferManager.SlotKey(station, slot)))
            {
                return slot;
            }
        }

        return 0;
    }

    /// <summary>
    /// 哪台机械手现在能接这一趟：两个站点都在它的站点表里、能接单（<see cref="IsRobotFree"/>）、有一只空着且两边都许用的手；没有返回 null。
    /// </summary>
    protected virtual string? RobotFor(string source, string target, ISet<string> claimedRobots)
    {
        var transfers = _transfers;
        var ledger = _ledger;
        if (transfers is null || ledger is null)
        {
            return null;
        }

        foreach (var robot in transfers.Robots)
        {
            if (!IsRobotFree(robot, claimedRobots) || !robot.TryGetStation(source, out var from) || !robot.TryGetStation(target, out var to))
            {
                continue;
            }

            int arms = ledger.GetSlots(robot.Name).Count;
            for (int arm = 1; arm <= arms; arm++)
            {
                if (from.AllowsArm(arm) && to.AllowsArm(arm) && ledger.Get(robot.Name, arm) is null && !_locks.IsArmLocked(robot.Name, arm))
                {
                    return robot.Name;
                }
            }
        }

        return null;
    }

    /// <summary>机械手能接单：启用、空闲、手上没单、这一拍还没派过。</summary>
    protected bool IsRobotFree(IRobot robot, ISet<string> claimedRobots)
    {
        if (claimedRobots.Contains(robot.Name) || _locks.IsRobotBusy(robot.Name) || robot.State != ModuleState.Idle)
        {
            return false;
        }

        return robot is not BaseModule module || module.IsEnabled;
    }

    #endregion

    /// <summary>
    /// 找机械手、交给搬运管理：受理了就把取片、放片记成进行中、记下这一趟等结果，占住目标槽和机械手，返回 true。
    /// 只放片（片在机械手手上）就用拿着片的那台。
    /// </summary>
    private bool Submit(TaskRow row, WaferTask? pick, WaferTask place, TaskLocation source, string target, int targetSlot,
        BaseTaskComponent tasks, ISet<string> claimedSlots, ISet<string> claimedRobots)
    {
        var transfers = _transfers!;
        string? robot;
        if (source.IsArm)
        {
            robot = transfers.TryGetRobot(source.Module, out var holder) && IsRobotFree(holder, claimedRobots) ? holder.Name : null;
        }
        else
        {
            robot = RobotFor(source.Module, target, claimedRobots);
        }

        if (robot is null)
        {
            return false;
        }

        var ticket = transfers.Submit(new TransferRequest
        {
            Origin = TransferOrigin.Auto,
            Owner = row.Owner,
            WaferId = row.WaferId,
            Source = source.Module,
            SourceSlot = source.Slot,
            Target = target,
            TargetSlot = targetSlot,
            Robot = robot,
        });
        var completion = ticket.Completion;
        if (!ticket.Accepted || completion is null)
        {
            return false;
        }

        if (pick is not null)
        {
            tasks.Start(row, pick, source.Module, source.Slot, robot);
        }

        tasks.Start(row, place, target, targetSlot, robot);
        _moves.Add(new Move(row, pick, place, completion));
        claimedSlots.Add(TransferManager.SlotKey(target, targetSlot));
        claimedRobots.Add(robot);
        return true;
    }
}
