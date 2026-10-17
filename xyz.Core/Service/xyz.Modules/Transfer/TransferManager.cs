using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Shared.Errors;
using xyz.Shared.Dtos;

namespace xyz.Modules;

[Component(description: "搬运执行：校验参数、占用槽位和手臂、驱动站点交互与机械手取放")]
public class TransferManager : ComponentBase, ITransferManager
{

    #region EC

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

    #endregion

    #region SC

    [SCEditor("True", "Transfer", "是否启用搬运（False=不启动搬运操作，设备照常点动）")]
    public bool IsEnable { get; set; } = true;

    #endregion

    #region SV

    /// <summary>

    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "自动派单是否开启")]
    public bool IsAutoDispatch { get; private set; }

    /// <summary>

    public void StartAutoDispatch()
    {
        SetAutoDispatch(true);
    }

    /// <summary>
    /// 关自动调度：Job 不再启动新任务；正在执行的操作继续完成。
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

    private readonly object _gate = new();
    private readonly Dictionary<string, TransferRoutine> _running = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<Guid, TransferRoutine> _held = new();

    private readonly Dictionary<string, TransferRoutine> _slotLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TransferRoutine> _armLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, TransferRoutine> _waferLocks = new();

    public TransferManager()
    {
        Register<ITransferManager>(this);
    }


    #region 模块装配

    private IReadOnlyDictionary<string, IModule> _modules =new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<IRobot> _robots = [];

    public IReadOnlyList<IRobot> Robots => _robots;

    public IEnumerable<ITransferStation> Stations => _modules.Values.OfType<ITransferStation>();

    /// <summary>
    /// 开机：认装起来的站点和机械手
    /// </summary>
    public override void Bind()
    {
        Bind(FindAll<IModule>());
    }

    /// <summary>
    /// 按给的模块认站点和机械手（开机由 Bind() 传整棵树上的模块）
    /// </summary>
    public void Bind(IEnumerable<IModule> modules)
    {
        var table = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            table[module.Name] = module;
        }

        _modules = table;
        _robots = table.Values.OfType<IRobot>().ToList();

        LogHelper.Info(Name,
            $"搬运模块表 {table.Count} 个，机械手 {_robots.Count} 台：{string.Join(", ", _robots.Select(r => r.Name))}");
    }

    public bool TryGetStation(string name, [MaybeNullWhen(false)] out ITransferStation station)
    {
        station = _modules.TryGetValue(name, out var module) ? module as ITransferStation : null;
        return station is not null;
    }

    public bool TryGetRobot(string name, [MaybeNullWhen(false)] out IRobot robot)
    {
        robot = _modules.TryGetValue(name, out var module) ? module as IRobot : null;
        return robot is not null;
    }

    #endregion

    #region 执行搬运

    public HandleResult<TransferRoutine> Start(TransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        #region 前提：搬运、晶圆账 组件都开着

        if (!IsEnable)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferDisabled);
        }

        var ledger = ComponentBase.Find<IWaferManagerComponent>();  //晶圆账单管理
        if (ledger is null || !ledger.IsEnable)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.WaferLedgerDisabled);
        }

        #endregion

        #region 源：站点，或者拿着片的机械手（只放片）

        string sourceName = request.Source.Trim(); //源头站点
        ITransferStation? source = null;
        IRobot? holder = null;
        if (TryGetStation(sourceName, out var station))  //获取源头站点对象
        {
            source = station;
        }
        else if (TryGetRobot(sourceName, out var robot)) //获取这个robot对象
        {
            holder = robot;
        }
        else
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferStationNotFound, sourceName);
        }

        #endregion

        #region 目标：站点，或者机械手（只取片，源不能也是机械手）

        string targetName = request.Target.Trim();//目标站点
        ITransferStation? target = null;
        IRobot? receiver = null;
        if (TryGetStation(targetName, out var targetStation)) //获取目标站点对象
        {
            target = targetStation;
        }
        else if (holder is null && TryGetRobot(targetName, out var robotTarget))  //要是同一个robot
        {
            receiver = robotTarget;
        }
        else
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferStationNotFound, targetName);
        }

        #endregion

        #region 槽号：在范围内，源和目标不是同一个槽

        var rejected = source is not null ? CheckSlot(source, request.SourceSlot) : CheckArmSlot(sourceName, request.SourceSlot, ledger);
        rejected ??= target is not null ? CheckSlot(target, request.TargetSlot) : CheckArmSlot(targetName, request.TargetSlot, ledger);
        if (rejected is not null)
        {
            return rejected;
        }

        if (source is not null && target is not null && string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase)
            && request.SourceSlot == request.TargetSlot)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferSameSlot);
        }

        #endregion

        #region 准入并占资源：检查和占锁在同一把锁里，两趟不会同时通过

        TransferRoutine operation;
        lock (_gate)
        {
            var admitted = Admit(request, ledger, source, holder, sourceName, target, receiver, targetName);
            if (!admitted.IsSuccess || admitted.Result is null)
            {
                return admitted;
            }

            operation = admitted.Result;
            operation.DeferCompletion();
            Lock(operation);
            _running[operation.Robot.Name] = operation;
        }

        #endregion

        LogHelper.Info(Name, $"开始搬运：{operation.WaferName} {operation.Name}（{operation.Origin}）");
        return HandleResult<TransferRoutine>.Success(operation);
    }

    private static HandleResult<TransferRoutine>? CheckSlot(ITransferStation station, int slot)
    {
        if (slot >= 1 && slot <= station.SlotCount)
        {
            return null;
        }

        return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferSlotOutOfRange,
            station.Name, slot.ToString(CultureInfo.InvariantCulture), station.SlotCount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>源或目标是机械手时：手指号要在晶圆账给它登记的手指数以内。</summary>
    private static HandleResult<TransferRoutine>? CheckArmSlot(string robot, int arm, IWaferManagerComponent ledger)
    {
        int armCount = ledger.GetSlots(robot).Count;
        if (arm >= 1 && arm <= armCount)
        {
            return null;
        }

        return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferSlotOutOfRange,
            robot, arm.ToString(CultureInfo.InvariantCulture), armCount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 在资源锁内校验片、目标、归属、机械手和手臂；都过了返回造好的搬运工单，有一项不过返回原因（错误码 + 参数）。
    /// 源是机械手（holder 不为 null，只放片）时就用拿着片的那台、那只手；
    /// 目标是机械手（receiver 不为 null，只取片）时就用点了名的那台、目标槽号那只手。
    /// </summary>
    private HandleResult<TransferRoutine> Admit(TransferRequest request, IWaferManagerComponent ledger, ITransferStation? source, IRobot? holder, string sourceName,
        ITransferStation? target, IRobot? receiver, string targetName)
    {
        string sourceSlot = request.SourceSlot.ToString(CultureInfo.InvariantCulture);
        string targetSlot = request.TargetSlot.ToString(CultureInfo.InvariantCulture);

        #region 片：源槽上有片、就是要搬的那一片，目标槽空着

        var wafer = ledger.Get(sourceName, request.SourceSlot);  //账单上面就是这个源头是不是有片
        if (wafer is null)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.WaferNoWafer, sourceName, sourceSlot);
        }

        if (request.WaferId is Guid expected && wafer.Id != expected)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferWaferMismatch, sourceName, sourceSlot, wafer.WaferId);
        }

        var occupant = ledger.Get(targetName, request.TargetSlot); //账单上面就是这个目标站点不能有片
        if (occupant is not null)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.WaferSlotOccupied, targetName, targetSlot, occupant.WaferId);
        }

        #endregion

        #region 归属：被 Job 占着的片只能由那个 Job 搬（人工恢复放行）

        string? owner = ComponentBase.Find<IJobManager>()?.OwnerOf(wafer.Id); //这个wafer有没有被job占用
        if (owner is not null && request.Origin != TransferOrigin.Recovery&& !string.Equals(owner, request.Owner, StringComparison.Ordinal))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferWaferOwned, wafer.WaferId, owner);
        }

        #endregion

        #region 占用：这一片、源槽、目标槽没被别的搬运占着

        if (_waferLocks.ContainsKey(wafer.Id) || _slotLocks.ContainsKey(SlotKey(sourceName, request.SourceSlot)))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferSlotLocked, sourceName, sourceSlot);
        }

        if (_slotLocks.ContainsKey(SlotKey(targetName, request.TargetSlot)))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferSlotLocked, targetName, targetSlot);
        }

        #endregion

        #region 机械手和手：只放片用拿着片的那只，只取片用点名的那只，取放一趟自己挑

        IRobot robot;
        int arm;
        if (holder is not null)
        {
            var heldError = UseHoldingArm(request, holder, targetName, out arm);
            if (heldError is not null)
            {
                return heldError;
            }

            robot = holder;
        }
        else if (receiver is not null)
        {
            var receiveError = UseReceivingArm(request, receiver, sourceName, out arm);
            if (receiveError is not null)
            {
                return receiveError;
            }

            robot = receiver;
        }
        else
        {
            var robotError = PickRobot(request.Robot, sourceName, targetName, out robot);
            if (robotError is not null)
            {
                return robotError;
            }

            var armError = PickArm(robot, request.Arm, sourceName, targetName, ledger, out arm);
            if (armError is not null)
            {
                return armError;
            }
        }

        #endregion

        #region 没在忙：这台机械手、这两个站点都没在跑别的搬运

        if (_running.ContainsKey(robot.Name))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.ActionRejected, robot.Name, robot.State.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var active in _running.Values)
        {
            if (string.Equals(active.SourceName, sourceName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(active.TargetName, sourceName, StringComparison.OrdinalIgnoreCase))
            {
                return HandleResult<TransferRoutine>.Fail(ErrorCodes.ActionRejected, sourceName, (source as IModule)?.State.ToString(CultureInfo.InvariantCulture) ?? robot.State.ToString(CultureInfo.InvariantCulture));
            }

            if (string.Equals(active.SourceName, targetName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(active.TargetName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return HandleResult<TransferRoutine>.Fail(ErrorCodes.ActionRejected, targetName, (target as IModule)?.State.ToString(CultureInfo.InvariantCulture) ?? robot.State.ToString(CultureInfo.InvariantCulture));
            }
        }

        #endregion

        var operation = new TransferRoutine(request.Origin, robot, source, request.SourceSlot,
            target, request.TargetSlot, arm, StationWaitTimeoutMs)
        {
            Owner = request.Owner,
            WaferId = wafer.Id,
            WaferName = wafer.WaferId,
        };
        return HandleResult<TransferRoutine>.Success(operation);
    }

    /// <summary>
    /// 挑机械手：点了名的要两个站点都到得了；没点名的按 sc.xml 先后挑第一台到得了的。
    /// </summary>
    private HandleResult<TransferRoutine>? PickRobot(string? wanted, string source, string target, out IRobot robot)
    {
        robot = null!;
        if (!string.IsNullOrWhiteSpace(wanted))
        {
            if (!TryGetRobot(wanted.Trim(), out var named))
            {
                return HandleResult<TransferRoutine>.Fail(ErrorCodes.ModuleNotFound, wanted.Trim());
            }

            if (!Reaches(named, source, target))
            {
                return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferNoRobot, source, target);
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

        return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferNoRobot, source, target);
    }

    private static bool Reaches(IRobot robot, string source, string target)
    {
        return robot.TryGetStation(source, out _) && robot.TryGetStation(target, out _);
    }

    /// <summary>
    /// 片已经在手上（只放片）：就用拿着它的那台、那只手。点了名的机械手要是这台、点了名的手要是这只；
    /// 目标站点要到得了、许用这只手，这只手没被别的操作占用。
    /// </summary>
    private HandleResult<TransferRoutine>? UseHoldingArm(TransferRequest request, IRobot holder, string target, out int arm)
    {
        arm = request.SourceSlot;
        string armText = arm.ToString(CultureInfo.InvariantCulture);
        string? wanted = request.Robot?.Trim();
        if ((!string.IsNullOrEmpty(wanted) && !string.Equals(wanted, holder.Name, StringComparison.OrdinalIgnoreCase))
            || !holder.TryGetStation(target, out var to))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferNoRobot, holder.Name, target);
        }

        if ((request.Arm > 0 && request.Arm != arm) || !to.AllowsArm(arm) || _armLocks.ContainsKey(ArmKey(holder.Name, arm)))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferArmUnavailable, holder.Name, armText);
        }

        return null;
    }

    /// <summary>
    /// 片要取到机械手手上（只取片）：就用目标这台、目标槽号那只手。点了名的机械手要是这台、点了名的手要是这只；
    /// 源站点要到得了、许用这只手，这只手没被别的操作占用（账上空着已经在前面按目标槽查过）。
    /// </summary>
    private HandleResult<TransferRoutine>? UseReceivingArm(TransferRequest request, IRobot receiver, string source, out int arm)
    {
        arm = request.TargetSlot;
        string armText = arm.ToString(CultureInfo.InvariantCulture);
        string? wanted = request.Robot?.Trim();
        if ((!string.IsNullOrEmpty(wanted) && !string.Equals(wanted, receiver.Name, StringComparison.OrdinalIgnoreCase))
            || !receiver.TryGetStation(source, out var from))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferNoRobot, source, receiver.Name);
        }

        if ((request.Arm > 0 && request.Arm != arm) || !from.AllowsArm(arm) || _armLocks.ContainsKey(ArmKey(receiver.Name, arm)))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferArmUnavailable, receiver.Name, armText);
        }

        return null;
    }

    /// <summary>
    /// 挑手臂：两个站点都许用、账上空着、没被别的操作占用。点了名的不行就拒，没点名的从 1 号手往上挑。
    /// 手指数按晶圆账给机械手登记的槽数（机械手组件初始化时按驱动轴表登记）。
    /// </summary>
    private HandleResult<TransferRoutine>? PickArm(IRobot robot, int wanted, string source, string target, IWaferManagerComponent ledger, out int arm)
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
                return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferArmUnavailable, robot.Name, wanted.ToString(CultureInfo.InvariantCulture));
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

        return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferNoArm, robot.Name);
    }

    #endregion

    #region 中止、恢复、占用查询

    /// <summary>请求中止这个操作，由搬运扫描线程执行设备中止和收尾。</summary>
    public bool Cancel(TransferRoutine operation, string reason)
    {
        return Cancel(active => ReferenceEquals(active, operation), reason) > 0;
    }

    public int CancelOwner(string owner, string reason)
    {
        return Cancel(operation => string.Equals(operation.Owner, owner, StringComparison.Ordinal), reason);
    }

    public int CancelAll(string reason)
    {
        return Cancel(_ => true, reason);
    }

    private int Cancel(Func<TransferRoutine, bool> match, string reason)
    {
        int marked = 0;
        lock (_gate)
        {
            foreach (var operation in _running.Values.Where(match))
            {
                if (!operation.IsTerminal && !operation.AbortRequested)
                {
                    operation.RequestAbort(reason);
                    marked++;
                }
            }
        }

        return marked;
    }

    /// <summary>人工确认片位后，释放这片晶圆因搬运失败保留的资源。</summary>
    public bool ReleaseHold(Guid waferId)
    {
        lock (_gate)
        {
            if (!_held.Remove(waferId, out var operation))
            {
                return false;
            }

            Unlock(operation);
        }

        LogHelper.Info(Name, $"晶圆 {waferId} 的搬运占用已人工放开（片位已确认）");
        return true;
    }

    /// <summary>动过手才失败，等待人工确认的操作；已完成的操作不留历史。</summary>
    public IReadOnlyList<TransferRoutine> HeldOperations
    {
        get
        {
            lock (_gate)
            {
                return _held.Values.ToList();
            }
        }
    }

    public bool IsRobotInTransfer(string robot)
    {
        lock (_gate)
        {
            return _running.ContainsKey(robot);
        }
    }

    /// <summary>槽位是否已被搬运操作占用；站点名不分大小写。</summary>
    public bool IsSlotLocked(string station, int slot)
    {
        lock (_gate)
        {
            return _slotLocks.ContainsKey(SlotKey(station, slot));
        }
    }

    #endregion

    #region 扫描

    /// <summary>推进当前搬运操作；调用设备在锁外做，避免与模块锁互等。</summary>
    protected override void OnScan()
    {
        base.OnScan();
        List<TransferRoutine> running;
        lock (_gate)
        {
            running = _running.Values.ToList();
        }

        foreach (var operation in running)
        {
            if (operation.AbortRequested && !operation.IsTerminal)
            {
                operation.AbortByHost(operation.AbortReason);
            }

            if (!operation.IsTerminal && IsEnable)
            {
                operation.Scan();
            }

            if (operation.ReadyToFinish)
            {
                Finish(operation);
            }
        }
    }

    private void Finish(TransferRoutine operation)
    {
        lock (_gate)
        {
            if (!_running.TryGetValue(operation.Robot.Name, out var active) || !ReferenceEquals(active, operation))
            {
                return;
            }

            _running.Remove(operation.Robot.Name);
            if (operation.NeedsRecovery)
            {
                _held[operation.WaferId] = operation;
            }
            else
            {
                Unlock(operation);
            }
        }

        if (operation.IsSuccess)
        {
            LogHelper.Info(Name, $"搬运完成：{operation.WaferName} {operation.Name}");
        }
        else if (operation.NeedsRecovery)
        {
            LogHelper.Error(Name, $"搬运失败，动过手了，资源保留到人工确认片位：{operation.WaferName} {operation.Name}（{operation.Reason}）");
        }
        else
        {
            LogHelper.Warn(Name, $"搬运未完成，片没动过，资源已释放：{operation.Name}（{operation.Reason}）");
        }

        operation.NotifyCompletion();
    }

    public override object? Abort()
    {
        base.Abort();
        StopAutoDispatch();
        CancelAll("搬运管理中止");
        return null;
    }

    #endregion

    #region 资源占用

    internal static string SlotKey(string station, int slot)
    {
        return $"{station}#{slot.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string ArmKey(string robot, int arm)
    {
        return $"{robot}#{arm.ToString(CultureInfo.InvariantCulture)}";
    }

    private void Lock(TransferRoutine operation)
    {
        _slotLocks[SlotKey(operation.SourceName, operation.SourceSlot)] = operation;
        _slotLocks[SlotKey(operation.TargetName, operation.TargetSlot)] = operation;
        _armLocks[ArmKey(operation.Robot.Name, operation.Arm)] = operation;
        _waferLocks[operation.WaferId] = operation;
    }

    private void Unlock(TransferRoutine operation)
    {
        RemoveIfOwned(_slotLocks, SlotKey(operation.SourceName, operation.SourceSlot), operation);
        RemoveIfOwned(_slotLocks, SlotKey(operation.TargetName, operation.TargetSlot), operation);
        RemoveIfOwned(_armLocks, ArmKey(operation.Robot.Name, operation.Arm), operation);
        if (_waferLocks.TryGetValue(operation.WaferId, out var holder) && ReferenceEquals(holder, operation))
        {
            _waferLocks.Remove(operation.WaferId);
        }
    }

    private static void RemoveIfOwned(Dictionary<string, TransferRoutine> locks, string key, TransferRoutine operation)
    {
        if (locks.TryGetValue(key, out var holder) && ReferenceEquals(holder, operation))
        {
            locks.Remove(key);
        }
    }

    #endregion
}
