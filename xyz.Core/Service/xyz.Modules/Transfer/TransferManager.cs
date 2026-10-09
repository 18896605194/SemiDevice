using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Errors;
using xyz.Shared.Dtos;

namespace xyz.Modules;

[Component(description: "搬运执行：校验参数、占用槽位和手臂、驱动站点交互与机械手取放")]
public class TransferManager : ComponentBase
{
    /// <summary>
    /// 当前搬运管理；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static TransferManager? Current { get; set; }

    private readonly object _gate = new();
    /// <summary>每台机械手当前执行的搬运操作。</summary>
    private readonly Dictionary<string, TransferRoutine> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>动过手才失败的操作，人工确认片位后按晶圆放锁。</summary>
    private readonly Dictionary<Guid, TransferRoutine> _held = new();

    private readonly Dictionary<string, TransferRoutine> _slotLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TransferRoutine> _armLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, TransferRoutine> _waferLocks = new();

    public TransferManager()
    {
        Current = this;
    }

    #region SC

    [SCEditor("True", "Transfer", "是否启用搬运（False=不启动搬运操作，设备照常点动）")]
    public bool IsEnable { get; set; } = true;

    #endregion

    #region SV

    /// <summary>
    /// 是否允许 Job 自动启动任务；关闭后正在执行的操作与手动传片仍正常推进。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "自动派单是否开启")]
    public bool IsAutoDispatch { get; private set; }

    /// <summary>
    /// 开自动调度：Job 按任务表启动设备操作。
    /// </summary>
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

    #region 模块表（装配后灌）

    private IReadOnlyDictionary<string, BaseModule> _modules =
        new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<IRobot> _robots = [];

    public IReadOnlyList<IRobot> Robots => _robots;

    /// <summary>模块表里的可服务工位（LoadPort、腔体、别的站点），先后同 sc.xml。</summary>
    public IEnumerable<ITransferStation> Stations => _modules.Values.OfType<ITransferStation>();

    /// <summary>
    /// 绑定模块表（装配完、模块起扫描之后调一次）。
    /// 搬运参数里的站点名就是模块名，靠这张表把名字解析成站点和机械手。
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

    #endregion

    #region 执行搬运

    /// <summary>
    /// 校验并启动一次搬运，返回实际执行的操作。资源被占用就拒绝，自动任务留在任务表里等下一拍。
    /// 手动与自动共用同一套检查、资源占用和取放流程。
    /// </summary>
    public HandleResult<TransferRoutine> Start(TransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsEnable)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferDisabled);
        }

        var ledger = WaferManagerComponent.Current;
        if (ledger is null || !ledger.IsEnable)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.WaferLedgerDisabled);
        }

        string sourceName = request.Source.Trim();
        string targetName = request.Target.Trim();
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
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferStationNotFound, sourceName);
        }

        if (!TryGetStation(targetName, out var target))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferStationNotFound, targetName);
        }

        var rejected = source is not null ? CheckSlot(source, request.SourceSlot) : CheckArmSlot(sourceName, request.SourceSlot, ledger);
        rejected ??= CheckSlot(target, request.TargetSlot);
        if (rejected is not null)
        {
            return rejected;
        }

        if (source is not null && string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase)
            && request.SourceSlot == request.TargetSlot)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferSameSlot);
        }

        TransferRoutine operation;
        lock (_gate)
        {
            rejected = Admit(request, ledger, source, holder, sourceName, target, out operation);
            if (rejected is not null)
            {
                return rejected;
            }

            operation.DeferCompletion();
            Lock(operation);
            _running[operation.Robot.Name] = operation;
        }

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

    /// <summary>源是机械手时：手指号要在晶圆账给它登记的手指数以内。</summary>
    private static HandleResult<TransferRoutine>? CheckArmSlot(string robot, int arm, WaferManagerComponent ledger)
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
    /// 在资源锁内校验片、目标、归属、机械手和手臂；通过后创建实际执行的操作。
    /// 源是机械手（holder 不为 null，只放片）时就用拿着片的那台、那只手。
    /// </summary>
    private HandleResult<TransferRoutine>? Admit(TransferRequest request, WaferManagerComponent ledger, ITransferStation? source, IRobot? holder, string sourceName,
        ITransferStation target, out TransferRoutine operation)
    {
        operation = null!;
        string sourceSlot = request.SourceSlot.ToString(CultureInfo.InvariantCulture);
        string targetSlot = request.TargetSlot.ToString(CultureInfo.InvariantCulture);

        var wafer = ledger.Get(sourceName, request.SourceSlot);
        if (wafer is null)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.WaferNoWafer, sourceName, sourceSlot);
        }

        if (request.WaferId is Guid expected && wafer.Id != expected)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferWaferMismatch, sourceName, sourceSlot, wafer.WaferId);
        }

        var occupant = ledger.Get(target.Name, request.TargetSlot);
        if (occupant is not null)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.WaferSlotOccupied, target.Name, targetSlot, occupant.WaferId);
        }

        // 被 Job 占着的片：Job 只能搬自己的；手动传片拒绝；人工确认片位后的恢复操作放行。
        string? owner = JobManager.Current?.OwnerOf(wafer.Id);
        if (owner is not null && request.Origin != TransferOrigin.Recovery
            && !string.Equals(owner, request.Owner, StringComparison.Ordinal))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferWaferOwned, wafer.WaferId, owner);
        }

        if (_waferLocks.ContainsKey(wafer.Id) || _slotLocks.ContainsKey(SlotKey(sourceName, request.SourceSlot)))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferSlotLocked, sourceName, sourceSlot);
        }

        if (_slotLocks.ContainsKey(SlotKey(target.Name, request.TargetSlot)))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.TransferSlotLocked, target.Name, targetSlot);
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

        if (_running.ContainsKey(robot.Name))
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.ActionRejected, robot.Name, robot.State.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var active in _running.Values)
        {
            if (string.Equals(active.SourceName, sourceName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(active.Target.Name, sourceName, StringComparison.OrdinalIgnoreCase))
            {
                return HandleResult<TransferRoutine>.Fail(ErrorCodes.ActionRejected, sourceName, (source as BaseModule)?.State.ToString(CultureInfo.InvariantCulture) ?? robot.State.ToString(CultureInfo.InvariantCulture));
            }

            if (string.Equals(active.SourceName, target.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(active.Target.Name, target.Name, StringComparison.OrdinalIgnoreCase))
            {
                return HandleResult<TransferRoutine>.Fail(ErrorCodes.ActionRejected, target.Name, (target as BaseModule)?.State.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        operation = new TransferRoutine(request.Origin, robot, source, request.SourceSlot,
            target, request.TargetSlot, arm, StationWaitTimeoutMs)
        {
            Owner = request.Owner,
            WaferId = wafer.Id,
            WaferName = wafer.WaferId,
        };
        return null;
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
    /// 挑手臂：两个站点都许用、账上空着、没被别的操作占用。点了名的不行就拒，没点名的从 1 号手往上挑。
    /// 手指数按晶圆账给机械手登记的槽数（机械手组件初始化时按驱动轴表登记）。
    /// </summary>
    private HandleResult<TransferRoutine>? PickArm(IRobot robot, int wanted, string source, string target, WaferManagerComponent ledger, out int arm)
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
        _slotLocks[SlotKey(operation.Target.Name, operation.TargetSlot)] = operation;
        _armLocks[ArmKey(operation.Robot.Name, operation.Arm)] = operation;
        _waferLocks[operation.WaferId] = operation;
    }

    private void Unlock(TransferRoutine operation)
    {
        RemoveIfOwned(_slotLocks, SlotKey(operation.SourceName, operation.SourceSlot), operation);
        RemoveIfOwned(_slotLocks, SlotKey(operation.Target.Name, operation.TargetSlot), operation);
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
