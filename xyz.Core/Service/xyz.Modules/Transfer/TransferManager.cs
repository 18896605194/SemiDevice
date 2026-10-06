using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Errors;

namespace xyz.Modules;

[Component(description: "搬运管理：受理搬运单、锁槽位和手臂、驱动站点交互环与机械手取放")]
public class TransferManager : ComponentBase
{
    /// <summary>
    /// 当前搬运管理；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static TransferManager? Current { get; set; }

    private readonly object _gate = new();
    private long _nextId;

    /// <summary>排着还没开始的单，先来先跑。</summary>
    private readonly List<TransferOrder> _queue = [];

    /// <summary>在跑的单：机械手名 → 单（一台机械手一次一张）。只有扫描线程增删。</summary>
    private readonly Dictionary<string, TransferOrder> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>动过手才失败的单：锁不放，等人工确认片位后 <see cref="ReleaseHold"/>。</summary>
    private readonly Dictionary<long, TransferOrder> _held = new();

    /// <summary>锁住的槽（站点#槽号）→ 单号。</summary>
    private readonly Dictionary<string, long> _slotLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>锁住的手臂（机械手#手指号）→ 单号。</summary>
    private readonly Dictionary<string, long> _armLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>在单子里的片 → 单号。</summary>
    private readonly Dictionary<Guid, long> _waferLocks = new();

    /// <summary>最近的结果（单号 → 结果），按 ResultKeepCount 留。</summary>
    private readonly Dictionary<long, TransferResult> _results = new();

    private readonly Queue<long> _resultOrder = new();

    public TransferManager()
    {
        Current = this;
    }

    #region SC

    [SCEditor("True", "Transfer", "是否启用搬运（False=不执行任何搬运单，设备照常点动）")]
    public bool IsEnable { get; set; } = true;

    #endregion

    #region SV

    /// <summary>
    /// 自动派单是否开启（SV）：只管"Job 要不要自己生成搬运单"，不管"要不要执行搬运单"。
    /// 关掉之后手动下的单照跑——执行搬运单是本组件的本职，自动派单只是其中一路输入。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "自动派单是否开启")]
    public bool IsAutoDispatch { get; private set; }

    /// <summary>
    /// 开自动派单：Job 开始按工艺自己生成搬运单。
    /// </summary>
    public void StartAutoDispatch()
    {
        SetAutoDispatch(true);
    }

    /// <summary>
    /// 关自动派单：Job 不再生成新单；已经在跑的单跑完自己那一趟。
    /// </summary>
    public void StopAutoDispatch()
    {
        SetAutoDispatch(false);
    }

    private void SetAutoDispatch(bool enabled)
    {
        if (IsAutoDispatch == enabled)
        {
            return;
        }

        IsAutoDispatch = enabled;
        LogHelper.Info(Name, enabled ? "自动派单已开启" : "自动派单已关闭");
    }

    #endregion

    #region 模块表（装配后灌）

    private IReadOnlyDictionary<string, BaseModule> _modules =
        new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<IRobot> _robots = [];

    public IReadOnlyList<IRobot> Robots => _robots;

    /// <summary>模块表里的可服务工位（LoadPort、腔体、别的站点），先后同 sc.xml。</summary>
    public IEnumerable<ITransferStation> Stations => _modules.Values.OfType<ITransferStation>();

    /// <summary>
    /// 绑定模块表（装配完、模块起扫描之后调一次）。
    /// 搬运单里的站点名就是模块名，靠这张表把名字解析成站点和机械手。
    /// </summary>
    public void Bind(IEnumerable<BaseModule> modules)
    {
        var table = new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            table[module.Name] = module;
        }

        _modules = table;
        _robots = table.Values.OfType<IRobot>().ToList();

        LogHelper.Info(Name,
            $"搬运模块表 {table.Count} 个，机械手 {_robots.Count} 台：{string.Join(", ", _robots.Select(r => r.Name))}");
    }

    /// <summary>
    /// 按模块名取可服务工位；名字不在表里、或那个模块不是可服务工位，返回 false。
    /// </summary>
    public bool TryGetStation(string name, [MaybeNullWhen(false)] out ITransferStation station)
    {
        station = _modules.TryGetValue(name, out var module) ? module as ITransferStation : null;
        return station is not null;
    }

    /// <summary>
    /// 按模块名取机械手；名字不在表里、或那个模块不是机械手，返回 false。
    /// </summary>
    public bool TryGetRobot(string name, [MaybeNullWhen(false)] out IRobot robot)
    {
        robot = _modules.TryGetValue(name, out var module) ? module as IRobot : null;
        return robot is not null;
    }

    #endregion

    #region EC 在线参数

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "10", max: "5000",
        @default: "200", description: "单周期慢扫描警告阈值")]
    public int SlowScanWarnMs
    {
        get { return GetEcInt(nameof(SlowScanWarnMs)); }
        set { SetEcInt(nameof(SlowScanWarnMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "10", max: "5000",
        @default: "300", description: "单周期慢扫描报警阈值")]
    public int SlowScanAlarmMs
    {
        get { return GetEcInt(nameof(SlowScanAlarmMs)); }
        set { SetEcInt(nameof(SlowScanAlarmMs), value); }
    }

    protected override int SlowScanWarnMilliseconds => SlowScanWarnMs;

    protected override int SlowScanAlarmMilliseconds => SlowScanAlarmMs;

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "60000", description: "搬运时等站点空出来（回到待命）的上限；等不到这一趟判失败，片没动过")]
    public int StationWaitTimeoutMs
    {
        get { return GetEcInt(nameof(StationWaitTimeoutMs)); }
        set { SetEcInt(nameof(StationWaitTimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "3600000",
        @default: "300000", description: "手动传片时界面等这一趟做完的上限；等不到界面先回超时，搬运照样跑完")]
    public int ManualWaitTimeoutMs
    {
        get { return GetEcInt(nameof(ManualWaitTimeoutMs)); }
        set { SetEcInt(nameof(ManualWaitTimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "10", max: "10000",
        @default: "200", description: "留最近多少张搬运单的结果供查询")]
    public int ResultKeepCount
    {
        get { return GetEcInt(nameof(ResultKeepCount)); }
        set { SetEcInt(nameof(ResultKeepCount), value); }
    }

    #endregion

    #region 下单

    /// <summary>
    /// 下一张搬运单：在锁里校验（站点、槽位、片、归属、锁、机械手、手臂），通过就把源槽、目标槽、手臂、片锁上、排进队列。
    /// 任意线程可调，当场回受理结果；搬没搬成看回执里的 Completion。
    /// </summary>
    public TransferTicket Submit(TransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsEnable)
        {
            return TransferTicket.Reject(ErrorCodes.TransferDisabled);
        }

        var ledger = WaferManager.Current;
        if (ledger is null || !ledger.IsEnable)
        {
            return TransferTicket.Reject(ErrorCodes.WaferLedgerDisabled);
        }

        string sourceName = request.Source.Trim();
        string targetName = request.Target.Trim();

        // 源一般是站点；片已经在机械手手上（重启前搬到一半、手动取了没放）时源是机械手、源槽是手指号，只放片
        ITransferStation? source = null;
        IRobot? holder = null;
        if (TryGetStation(sourceName, out var station))
        {
            source = station;
        }
        else if (TryGetRobot(sourceName, out var robot))
        {
            holder = robot;
        }
        else
        {
            return TransferTicket.Reject(ErrorCodes.TransferStationNotFound, sourceName);
        }

        if (!TryGetStation(targetName, out var target))
        {
            return TransferTicket.Reject(ErrorCodes.TransferStationNotFound, targetName);
        }

        var sourceError = source is not null ? CheckSlot(source, request.SourceSlot) : CheckArmSlot(sourceName, request.SourceSlot, ledger);
        var slotError = sourceError ?? CheckSlot(target, request.TargetSlot);
        if (slotError is not null)
        {
            return slotError;
        }

        if (source is not null && string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase)
            && request.SourceSlot == request.TargetSlot)
        {
            return TransferTicket.Reject(ErrorCodes.TransferSameSlot);
        }

        TransferOrder order;
        lock (_gate)
        {
            var rejected = Admit(request, ledger, source, holder, sourceName, target, out order);
            if (rejected is not null)
            {
                return rejected;
            }

            Lock(order);
            _queue.Add(order);
        }

        LogHelper.Info(Name, $"受理搬运单 #{order.Id}：{order.WaferName} {order.Describe()}（{order.Origin}{OwnerText(order.Owner)}）");
        return TransferTicket.Accept(order.Id, order.Completion.Task);
    }

    private static TransferTicket? CheckSlot(ITransferStation station, int slot)
    {
        if (slot >= 1 && slot <= station.SlotCount)
        {
            return null;
        }

        return TransferTicket.Reject(ErrorCodes.TransferSlotOutOfRange,
            station.Name, slot.ToString(CultureInfo.InvariantCulture), station.SlotCount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>源是机械手时：手指号要在晶圆账给它登记的手指数以内。</summary>
    private static TransferTicket? CheckArmSlot(string robot, int arm, WaferManager ledger)
    {
        int armCount = ledger.GetSlots(robot).Count;
        if (arm >= 1 && arm <= armCount)
        {
            return null;
        }

        return TransferTicket.Reject(ErrorCodes.TransferSlotOutOfRange,
            robot, arm.ToString(CultureInfo.InvariantCulture), armCount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 锁里的那部分校验：片、目标、归属、锁、机械手、手臂。通过返回 null 并给出要排队的单。
    /// 源是机械手（holder 不为 null，只放片）时就用拿着片的那台、那只手。
    /// </summary>
    private TransferTicket? Admit(TransferRequest request, WaferManager ledger, ITransferStation? source, IRobot? holder, string sourceName,
        ITransferStation target, out TransferOrder order)
    {
        order = null!;
        string sourceSlot = request.SourceSlot.ToString(CultureInfo.InvariantCulture);
        string targetSlot = request.TargetSlot.ToString(CultureInfo.InvariantCulture);

        var wafer = ledger.Get(sourceName, request.SourceSlot);
        if (wafer is null)
        {
            return TransferTicket.Reject(ErrorCodes.WaferNoWafer, sourceName, sourceSlot);
        }

        if (request.WaferId is Guid expected && wafer.Id != expected)
        {
            return TransferTicket.Reject(ErrorCodes.TransferWaferMismatch, sourceName, sourceSlot, wafer.WaferId);
        }

        var occupant = ledger.Get(target.Name, request.TargetSlot);
        if (occupant is not null)
        {
            return TransferTicket.Reject(ErrorCodes.WaferSlotOccupied, target.Name, targetSlot, occupant.WaferId);
        }

        // 被 Job 占着的片：Job 只能搬自己的，手动单一律拒，恢复单人工确认过才下得来所以放行
        string? owner = JobManager.Current?.OwnerOf(wafer.Id);
        if (owner is not null && request.Origin != TransferOrigin.Recovery
            && !string.Equals(owner, request.Owner, StringComparison.Ordinal))
        {
            return TransferTicket.Reject(ErrorCodes.TransferWaferOwned, wafer.WaferId, owner);
        }

        if (_waferLocks.ContainsKey(wafer.Id) || _slotLocks.ContainsKey(SlotKey(sourceName, request.SourceSlot)))
        {
            return TransferTicket.Reject(ErrorCodes.TransferSlotLocked, sourceName, sourceSlot);
        }

        if (_slotLocks.ContainsKey(SlotKey(target.Name, request.TargetSlot)))
        {
            return TransferTicket.Reject(ErrorCodes.TransferSlotLocked, target.Name, targetSlot);
        }

        IRobot robot;
        int arm;
        if (holder is not null)
        {
            var heldError = UseHoldingArm(request, holder, target.Name, out arm);
            if (heldError is not null)
            {
                return heldError;
            }

            robot = holder;
        }
        else
        {
            var robotError = PickRobot(request.Robot, sourceName, target.Name, out robot);
            if (robotError is not null)
            {
                return robotError;
            }

            var armError = PickArm(robot, request.Arm, sourceName, target.Name, ledger, out arm);
            if (armError is not null)
            {
                return armError;
            }
        }

        order = new TransferOrder
        {
            Id = ++_nextId,
            Origin = request.Origin,
            Owner = request.Owner,
            WaferId = wafer.Id,
            WaferName = wafer.WaferId,
            Robot = robot,
            Source = source,
            SourceName = sourceName,
            SourceSlot = request.SourceSlot,
            Target = target,
            TargetSlot = request.TargetSlot,
            Arm = arm,
            CreatedAt = DateTime.Now,
        };
        return null;
    }

    /// <summary>
    /// 挑机械手：点了名的要两个站点都到得了；没点名的按 sc.xml 先后挑第一台到得了的。
    /// </summary>
    private TransferTicket? PickRobot(string? wanted, string source, string target, out IRobot robot)
    {
        robot = null!;
        if (!string.IsNullOrWhiteSpace(wanted))
        {
            if (!TryGetRobot(wanted.Trim(), out var named))
            {
                return TransferTicket.Reject(ErrorCodes.ModuleNotFound, wanted.Trim());
            }

            if (!Reaches(named, source, target))
            {
                return TransferTicket.Reject(ErrorCodes.TransferNoRobot, source, target);
            }

            robot = named;
            return null;
        }

        foreach (var candidate in _robots)
        {
            if (Reaches(candidate, source, target))
            {
                robot = candidate;
                return null;
            }
        }

        return TransferTicket.Reject(ErrorCodes.TransferNoRobot, source, target);
    }

    private static bool Reaches(IRobot robot, string source, string target)
    {
        return robot.TryGetStation(source, out _) && robot.TryGetStation(target, out _);
    }

    /// <summary>
    /// 片已经在手上（只放片）：就用拿着它的那台、那只手。点了名的机械手要是这台、点了名的手要是这只；
    /// 目标站点要到得了、许用这只手，这只手没被别的单占着。
    /// </summary>
    private TransferTicket? UseHoldingArm(TransferRequest request, IRobot holder, string target, out int arm)
    {
        arm = request.SourceSlot;
        string armText = arm.ToString(CultureInfo.InvariantCulture);
        string? wanted = request.Robot?.Trim();
        if ((!string.IsNullOrEmpty(wanted) && !string.Equals(wanted, holder.Name, StringComparison.OrdinalIgnoreCase))
            || !holder.TryGetStation(target, out var to))
        {
            return TransferTicket.Reject(ErrorCodes.TransferNoRobot, holder.Name, target);
        }

        if ((request.Arm > 0 && request.Arm != arm) || !to.AllowsArm(arm) || _armLocks.ContainsKey(ArmKey(holder.Name, arm)))
        {
            return TransferTicket.Reject(ErrorCodes.TransferArmUnavailable, holder.Name, armText);
        }

        return null;
    }

    /// <summary>
    /// 挑手臂：两个站点都许用、账上空着、没被别的单占着。点了名的不行就拒，没点名的从 1 号手往上挑。
    /// 手指数按晶圆账给机械手登记的槽数（机械手 Open 时按驱动轴表登记）。
    /// </summary>
    private TransferTicket? PickArm(IRobot robot, int wanted, string source, string target, WaferManager ledger, out int arm)
    {
        arm = 0;
        int armCount = ledger.GetSlots(robot.Name).Count;
        robot.TryGetStation(source, out var from);
        robot.TryGetStation(target, out var to);

        bool Usable(int candidate)
        {
            return candidate >= 1 && candidate <= armCount
                && from is not null && from.AllowsArm(candidate)
                && to is not null && to.AllowsArm(candidate)
                && ledger.Get(robot.Name, candidate) is null
                && !_armLocks.ContainsKey(ArmKey(robot.Name, candidate));
        }

        if (wanted > 0)
        {
            if (!Usable(wanted))
            {
                return TransferTicket.Reject(ErrorCodes.TransferArmUnavailable, robot.Name, wanted.ToString(CultureInfo.InvariantCulture));
            }

            arm = wanted;
            return null;
        }

        for (int candidate = 1; candidate <= armCount; candidate++)
        {
            if (Usable(candidate))
            {
                arm = candidate;
                return null;
            }
        }

        return TransferTicket.Reject(ErrorCodes.TransferNoArm, robot.Name);
    }

    #endregion

    #region 撤单、恢复、查询

    /// <summary>
    /// 撤一张单：还没开始的直接撤掉（片没动过，锁放开）；在跑的标记中止，扫描线程下一拍撤（手臂在动的先发设备中止）。
    /// 返回撤了几张（含标记的）。
    /// </summary>
    public int Cancel(long id, string reason)
    {
        return Cancel(order => order.Id == id, reason);
    }

    /// <summary>
    /// 撤一个 Job（PJ 名）下的所有单：Job 中止时用。
    /// </summary>
    public int CancelOwner(string owner, string reason)
    {
        return Cancel(order => string.Equals(order.Owner, owner, StringComparison.Ordinal), reason);
    }

    /// <summary>
    /// 撤全部单：整机停止、搬运管理中止时用。
    /// </summary>
    public int CancelAll(string reason)
    {
        return Cancel(_ => true, reason);
    }

    private int Cancel(Func<TransferOrder, bool> match, string reason)
    {
        var dropped = new List<(TransferOrder Order, TransferResult Result)>();
        int marked = 0;
        lock (_gate)
        {
            foreach (var order in _queue.Where(match).ToList())
            {
                _queue.Remove(order);
                Unlock(order);
                var result = order.ToResult(TransferOutcome.Cancelled, ErrorCodes.TransferCancelled,
                    [order.Id.ToString(CultureInfo.InvariantCulture)], needsRecovery: false, picked: false);
                Remember(result);
                dropped.Add((order, result));
            }

            foreach (var order in _running.Values.Where(match))
            {
                if (!order.AbortRequested)
                {
                    order.AbortReason = reason;
                    order.AbortRequested = true;
                    marked++;
                }
            }
        }

        foreach (var (order, result) in dropped)
        {
            LogHelper.Info(Name, $"搬运单 #{order.Id} 还没开始就撤了（{reason}）：{order.WaferName} 没动过");
            order.Completion.TrySetResult(result);
        }

        return dropped.Count + marked;
    }

    /// <summary>
    /// 人工确认过片位，放开一张出错单留着的锁（设置 → 账单调整页把账对好之后）。没这张留着的单返回 false。
    /// </summary>
    public bool ReleaseHold(long id)
    {
        lock (_gate)
        {
            if (!_held.Remove(id, out var order))
            {
                return false;
            }

            Unlock(order);
        }

        LogHelper.Info(Name, $"搬运单 #{id} 的锁已人工放开（片位已确认）");
        return true;
    }

    /// <summary>
    /// 出错后留着锁、等人工确认的单（结果）。
    /// </summary>
    public IReadOnlyList<TransferResult> HeldResults
    {
        get
        {
            lock (_gate)
            {
                return _held.Keys
                    .Select(id => _results.TryGetValue(id, out var result) ? result : null)
                    .Where(result => result is not null)
                    .Select(result => result!)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// 查一张单的结果（留最近 ResultKeepCount 张）；还没结束或太老了返回 false。
    /// </summary>
    public bool TryGetResult(long id, [MaybeNullWhen(false)] out TransferResult result)
    {
        lock (_gate)
        {
            return _results.TryGetValue(id, out result);
        }
    }

    /// <summary>
    /// 这台机械手正在跑搬运单（整机停止时由搬运管理撤单、手臂在动的发设备中止，不另外直接发）。
    /// </summary>
    public bool IsRobotInTransfer(string robot)
    {
        lock (_gate)
        {
            return _running.ContainsKey(robot);
        }
    }

    /// <summary>
    /// 此刻的占用快照：调度每拍取一份，挑目标、挑手臂时避开锁着的。
    /// </summary>
    public TransferView GetView()
    {
        lock (_gate)
        {
            var busy = _queue.Select(order => order.Robot.Name).Concat(_running.Keys).ToList();
            return new TransferView(_slotLocks.Keys.ToList(), _armLocks.Keys.ToList(), _waferLocks.Keys.ToList(), busy);
        }
    }

    #endregion

    #region 扫描

    /// <summary>
    /// 扫描周期：先推进在跑的单（收尾的出结果），再给闲着的机械手开下一张。
    /// 本组件有自己的扫描线程，在所有模块起来之后由装配显式 Start。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();

        if (!IsEnable)
        {
            return;
        }

        StepRunning();
        StartQueued();
    }

    /// <summary>
    /// 推进在跑的单。推进在锁外做：一趟搬运会调到机械手、站点（它们有自己的锁），别拿着本组件的锁去等它们。
    /// </summary>
    private void StepRunning()
    {
        List<TransferOrder> running;
        lock (_gate)
        {
            running = _running.Values.ToList();
        }

        foreach (var order in running)
        {
            var routine = order.Routine;
            if (routine is null)
            {
                continue;
            }

            if (order.AbortRequested && !routine.IsTerminal)
            {
                // 手臂正在取放：光撤软件上的操作停不住手臂，先给机械手发设备中止（它会顶掉在途的取放）。
                if (routine.IsMoving)
                {
                    routine.Robot.Abort();
                }

                routine.AbortByHost(order.AbortReason);
            }

            if (!routine.IsTerminal)
            {
                routine.Scan();
            }

            if (routine.IsTerminal)
            {
                Finish(order, routine);
            }
        }
    }

    /// <summary>
    /// 一张单结束：成功或没动过手 → 放锁；动过手才失败 → 锁留着等人工确认。出结果（锁外）。
    /// </summary>
    private void Finish(TransferOrder order, TransferRoutine routine)
    {
        bool hold = !routine.IsSuccess && routine.MotionStarted;
        var outcome = routine.IsSuccess
            ? TransferOutcome.Completed
            : routine.State == OperationState.Aborted ? TransferOutcome.Aborted : TransferOutcome.Failed;
        var result = order.ToResult(outcome, routine.Code, routine.ErrorArgs, hold, routine.HasPicked);

        lock (_gate)
        {
            _running.Remove(order.Robot.Name);
            if (hold)
            {
                _held[order.Id] = order;
            }
            else
            {
                Unlock(order);
            }

            Remember(result);
        }

        if (result.IsSuccess)
        {
            LogHelper.Info(Name, $"搬运单 #{order.Id} 完成：{order.WaferName} {order.Describe()}");
        }
        else if (hold)
        {
            LogHelper.Error(Name, $"搬运单 #{order.Id} {outcome}，动过手了，片位说不准：锁留着，到现场确认片在哪、对好账再放（{routine.Reason}）");
        }
        else
        {
            LogHelper.Warn(Name, $"搬运单 #{order.Id} {outcome}，片没动过，锁已放开（{routine.Reason}）");
        }

        order.Completion.TrySetResult(result);
    }

    /// <summary>
    /// 给闲着的机械手开下一张单（先来先跑）。跟在跑的单用到同一个站点的先等着：
    /// 两台机械手各抢一个站点再去抢对方那个会死等，所以站点不重叠才开。
    /// </summary>
    private void StartQueued()
    {
        var started = new List<TransferOrder>();
        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                return;
            }

            var busyStations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var running in _running.Values)
            {
                busyStations.Add(running.SourceName);
                busyStations.Add(running.Target.Name);
            }

            int waitTimeout = StationWaitTimeoutMs;
            foreach (var order in _queue.ToList())
            {
                if (_running.ContainsKey(order.Robot.Name)
                    || busyStations.Contains(order.SourceName)
                    || busyStations.Contains(order.Target.Name))
                {
                    continue;
                }

                order.Routine = new TransferRoutine(order.Origin, order.Robot, order.Source, order.SourceSlot,
                    order.Target, order.TargetSlot, order.Arm, waitTimeout);
                order.StartedAt = DateTime.Now;
                _queue.Remove(order);
                _running[order.Robot.Name] = order;
                busyStations.Add(order.SourceName);
                busyStations.Add(order.Target.Name);
                started.Add(order);
            }
        }

        foreach (var order in started)
        {
            LogHelper.Info(Name, $"开始搬运单 #{order.Id}：{order.WaferName} {order.Describe()}");
        }
    }

    #endregion

    #region 中止

    /// <summary>
    /// 中止：先关自动派单（急停之后不能再派出新的一趟），再撤全部单——没开始的直接撤，在跑的下一拍撤（手臂在动的发设备中止）。
    /// 出错留着锁的单不动，等人工确认。
    /// </summary>
    public override object? Abort()
    {
        base.Abort();
        StopAutoDispatch();
        CancelAll("搬运管理中止");
        return null;
    }

    #endregion

    #region 锁

    /// <summary>槽锁的键（站点名不分大小写，交给字典的比较器）。</summary>
    internal static string SlotKey(string station, int slot)
    {
        return $"{station}#{slot.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>手臂锁的键。</summary>
    internal static string ArmKey(string robot, int arm)
    {
        return $"{robot}#{arm.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>锁上这张单要用的槽、手臂、片（在锁里调）。</summary>
    private void Lock(TransferOrder order)
    {
        _slotLocks[SlotKey(order.SourceName, order.SourceSlot)] = order.Id;
        _slotLocks[SlotKey(order.Target.Name, order.TargetSlot)] = order.Id;
        _armLocks[ArmKey(order.Robot.Name, order.Arm)] = order.Id;
        _waferLocks[order.WaferId] = order.Id;
    }

    /// <summary>放开这张单的锁（在锁里调）；只放还记在这张单名下的。</summary>
    private void Unlock(TransferOrder order)
    {
        RemoveIfOwned(_slotLocks, SlotKey(order.SourceName, order.SourceSlot), order.Id);
        RemoveIfOwned(_slotLocks, SlotKey(order.Target.Name, order.TargetSlot), order.Id);
        RemoveIfOwned(_armLocks, ArmKey(order.Robot.Name, order.Arm), order.Id);
        if (_waferLocks.TryGetValue(order.WaferId, out long holder) && holder == order.Id)
        {
            _waferLocks.Remove(order.WaferId);
        }
    }

    private static void RemoveIfOwned(Dictionary<string, long> locks, string key, long id)
    {
        if (locks.TryGetValue(key, out long holder) && holder == id)
        {
            locks.Remove(key);
        }
    }

    /// <summary>记下结果，超出保留条数的最老的扔掉（留着锁的单的结果不扔，人工确认时还要看）。在锁里调。</summary>
    private void Remember(TransferResult result)
    {
        _results[result.Id] = result;
        _resultOrder.Enqueue(result.Id);
        int keep = Math.Max(1, ResultKeepCount);
        int guard = _resultOrder.Count;
        while (_resultOrder.Count > keep && guard-- > 0)
        {
            long oldest = _resultOrder.Dequeue();
            if (_held.ContainsKey(oldest))
            {
                _resultOrder.Enqueue(oldest);
                continue;
            }

            _results.Remove(oldest);
        }
    }

    private static string OwnerText(string? owner)
    {
        return string.IsNullOrEmpty(owner) ? string.Empty : $"，{owner}";
    }

    #endregion
}
