using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Errors;
using xyz.Shared.Dtos;
using xyz.Tools;

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
    private readonly Dictionary<string, TransferExecution> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 失败后等人工确认的执行记录；交换的两片共用一条，只记一次
    /// </summary>
    private readonly HashSet<TransferExecution> _held = [];

    /// <summary>
    /// 片 → 挂着它的执行记录；人工按任意一片放锁都能找到整条
    /// </summary>
    private readonly Dictionary<Guid, TransferExecution> _heldByWafer = new();

    private readonly Dictionary<string, TransferExecution> _slotLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TransferExecution> _armLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, TransferExecution> _waferLocks = new();


    #region 模块装配

    private IReadOnlyDictionary<string, IModule> _modules =new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<ITransferRobot> _robots = [];

    public IReadOnlyList<ITransferRobot> Robots => _robots;

    public IEnumerable<ITransferStation> Stations => _modules.Values.OfType<ITransferStation>();

    // 下面两个单例开机绑模块表时取；没装是 null
    private IWaferManagerComponent? _ledger;
    private IJobManager? _jobs;

    /// <summary>
    /// 开机：认装起来的站点和机械手
    /// </summary>
    public override void Bind()
    {
        Bind(IocHelper.GetServices<IModule>());
    }

    /// <summary>
    /// 按给的模块认站点和机械手（开机由 Bind() 传整棵树上的模块）；晶圆账、Job 管理也在这里取一次
    /// </summary>
    public void Bind(IEnumerable<IModule> modules)
    {
        _ledger = IocHelper.GetService<IWaferManagerComponent>();
        _jobs = IocHelper.GetService<IJobManager>();
        var table = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            table[module.Name] = module;
        }

        _modules = table;
        _robots = table.Values.OfType<ITransferRobot>().ToList();
        CheckHandoffSupport();

        LogHelper.Info(Name,
            $"搬运模块表 {table.Count} 个，机械手 {_robots.Count} 台：{string.Join(", ", _robots.Select(r => r.Name))}");
    }

    /// <summary>
    /// 开机查：配了中途交接的站点，到得了它的机械手都得能分段取放（机械手类声明了 ITransferRobotStep）；配错开机就报出来，别等到跑货时才拒
    /// </summary>
    private void CheckHandoffSupport()
    {
        foreach (var robot in _robots)
        {
            if (!robot.IsEnabled || robot is ITransferRobotStep)
            {
                continue;
            }

            foreach (string name in robot.Stations.Keys)
            {
                if (!TryGetStation(name, out var station) || !station.IsEnabled)
                {
                    continue;
                }

                if (station.HandoffOnPick || station.HandoffOnPlace)
                {
                    throw new InvalidOperationException(
                        $"sc.xml 站点 {name} 配了取放中途交接（HandoffOnPick / HandoffOnPlace），机械手 {robot.Name} 不能分段取放（没声明 ITransferRobotStep），到不了这个站");
                }
            }
        }
    }

    public bool TryGetStation(string name, [MaybeNullWhen(false)] out ITransferStation station)
    {
        station = _modules.TryGetValue(name, out var module) ? module as ITransferStation : null;
        return station is not null;
    }

    public bool TryGetRobot(string name, [MaybeNullWhen(false)] out ITransferRobot robot)
    {
        robot = _modules.TryGetValue(name, out var module) ? module as ITransferRobot : null;
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

        var ledger = _ledger;  //晶圆账单管理
        if (ledger is null || !ledger.IsEnable)
        {
            return HandleResult<TransferRoutine>.Fail(ErrorCodes.WaferLedgerDisabled);
        }

        #endregion

        #region 源：站点，或者拿着片的机械手（只放片）

        string sourceName = request.Source.Trim(); //源头站点
        ITransferStation? source = null;
        ITransferRobot? holder = null;
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
        ITransferRobot? receiver = null;
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
    private HandleResult<TransferRoutine> Admit(TransferRequest request, IWaferManagerComponent ledger, ITransferStation? source, ITransferRobot? holder, string sourceName,
        ITransferStation? target, ITransferRobot? receiver, string targetName)
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

        string? owner = _jobs?.OwnerOf(wafer.Id); //这个wafer有没有被job占用
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

        ITransferRobot robot;
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
            if (UsesLocation(active, sourceName))
            {
                return HandleResult<TransferRoutine>.Fail(ErrorCodes.ActionRejected, sourceName, source?.State.ToString(CultureInfo.InvariantCulture) ?? robot.State.ToString(CultureInfo.InvariantCulture));
            }

            if (UsesLocation(active, targetName))
            {
                return HandleResult<TransferRoutine>.Fail(ErrorCodes.ActionRejected, targetName, target?.State.ToString(CultureInfo.InvariantCulture) ?? robot.State.ToString(CultureInfo.InvariantCulture));
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
    private HandleResult<TransferRoutine>? PickRobot(string? wanted, string source, string target, out ITransferRobot robot)
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

    private static bool Reaches(ITransferRobot robot, string source, string target)
    {
        return robot.TryGetStation(source, out _) && robot.TryGetStation(target, out _);
    }

    /// <summary>
    /// 在跑的这一趟用着这个位置：清单里的站点，或者就是它那台机械手
    /// </summary>
    private static bool UsesLocation(TransferExecution active, string name)
    {
        return active.Claims.Stations.Contains(name, StringComparer.OrdinalIgnoreCase)
            || string.Equals(active.Robot.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 片已经在手上（只放片）：就用拿着它的那台、那只手。点了名的机械手要是这台、点了名的手要是这只；
    /// 目标站点要到得了、许用这只手，这只手没被别的操作占用。
    /// </summary>
    private HandleResult<TransferRoutine>? UseHoldingArm(TransferRequest request, ITransferRobot holder, string target, out int arm)
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
    private HandleResult<TransferRoutine>? UseReceivingArm(TransferRequest request, ITransferRobot receiver, string source, out int arm)
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
    private HandleResult<TransferRoutine>? PickArm(ITransferRobot robot, int wanted, string source, string target, IWaferManagerComponent ledger, out int arm)
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

    #region 同腔交换

    /// <summary>
    /// 受理一次同腔交换：站点、两只手、两片、归属、占用、忙不忙都在同一把锁里查完，一次占齐资源
    /// </summary>
    public HandleResult<SwapRoutine> StartSwap(SwapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsEnable)
        {
            return HandleResult<SwapRoutine>.Fail(ErrorCodes.TransferDisabled);
        }

        var ledger = _ledger;
        if (ledger is null || !ledger.IsEnable)
        {
            return HandleResult<SwapRoutine>.Fail(ErrorCodes.WaferLedgerDisabled);
        }

        string stationName = request.Station.Trim();
        if (!TryGetStation(stationName, out var station))
        {
            return HandleResult<SwapRoutine>.Fail(ErrorCodes.TransferStationNotFound, stationName);
        }

        string robotName = request.Robot.Trim();
        if (!TryGetRobot(robotName, out var robot))
        {
            return HandleResult<SwapRoutine>.Fail(ErrorCodes.ModuleNotFound, robotName);
        }

        SwapRoutine operation;
        lock (_gate)
        {
            var rejected = CheckSwap(request, robot, station, ledger, out string pickName, out string placeName);
            if (rejected is not null)
            {
                return HandleResult<SwapRoutine>.Fail(rejected.ErrorMessage, [.. rejected.Args]);
            }

            operation = new SwapRoutine(request, robot, station, pickName, placeName, StationWaitTimeoutMs);
            operation.DeferCompletion();
            Lock(operation);
            _running[robot.Name] = operation;
        }

        LogHelper.Info(Name, $"开始交换：{operation.WaferNames} {operation.Name}（{operation.Origin}）");
        return HandleResult<SwapRoutine>.Success(operation);
    }

    /// <summary>
    /// 交换能不能做（锁里查）：同一个槽先出后进，所以不套普通搬运"目标槽要空""同站同槽就拒"那两条
    /// </summary>
    private HandleResult? CheckSwap(SwapRequest request, ITransferRobot robot, ITransferStation station, IWaferManagerComponent ledger,
        out string pickName, out string placeName)
    {
        pickName = string.Empty;
        placeName = string.Empty;
        string slotText = request.Slot.ToString(CultureInfo.InvariantCulture);
        string pickArmText = request.PickArm.ToString(CultureInfo.InvariantCulture);
        string placeArmText = request.PlaceArm.ToString(CultureInfo.InvariantCulture);

        #region 槽、手：槽在范围内；两只不同的手都在手指数以内、这个站都许用

        if (request.Slot < 1 || request.Slot > station.SlotCount)
        {
            return HandleResult.Fail(ErrorCodes.TransferSlotOutOfRange, station.Name, slotText,
                station.SlotCount.ToString(CultureInfo.InvariantCulture));
        }

        if (!robot.TryGetStation(station.Name, out var config))
        {
            return HandleResult.Fail(ErrorCodes.TransferNoRobot, station.Name, robot.Name);
        }

        int armCount = ledger.GetSlots(robot.Name).Count;
        if (request.PickArm == request.PlaceArm || !IsArmUsable(request.PickArm, armCount, config))
        {
            return HandleResult.Fail(ErrorCodes.TransferArmUnavailable, robot.Name, pickArmText);
        }

        if (!IsArmUsable(request.PlaceArm, armCount, config))
        {
            return HandleResult.Fail(ErrorCodes.TransferArmUnavailable, robot.Name, placeArmText);
        }

        #endregion

        #region 片：站里正好是预期的旧片，放片手上正好是预期的新片，取片手空着

        var pickWafer = ledger.Get(station.Name, request.Slot);
        if (pickWafer is null)
        {
            return HandleResult.Fail(ErrorCodes.WaferNoWafer, station.Name, slotText);
        }

        if (pickWafer.Id != request.PickWafer)
        {
            return HandleResult.Fail(ErrorCodes.TransferWaferMismatch, station.Name, slotText, pickWafer.WaferId);
        }

        var placeWafer = ledger.Get(robot.Name, request.PlaceArm);
        if (placeWafer is null)
        {
            return HandleResult.Fail(ErrorCodes.RobotArmNoWafer, robot.Name, placeArmText);
        }

        if (placeWafer.Id != request.PlaceWafer)
        {
            return HandleResult.Fail(ErrorCodes.TransferWaferMismatch, robot.Name, placeArmText, placeWafer.WaferId);
        }

        var inHand = ledger.Get(robot.Name, request.PickArm);
        if (inHand is not null)
        {
            return HandleResult.Fail(ErrorCodes.RobotArmHasWafer, robot.Name, pickArmText, inHand.WaferId);
        }

        #endregion

        #region 归属：被 Job 占着的片只能由那个 Job 搬（人工恢复放行）

        var ownerError = CheckOwner(pickWafer.Id, pickWafer.WaferId, request.PickOwner, request.Origin)
            ?? CheckOwner(placeWafer.Id, placeWafer.WaferId, request.PlaceOwner, request.Origin);
        if (ownerError is not null)
        {
            return ownerError;
        }

        #endregion

        #region 占用：站点这一槽、两只手、两片都没被别的操作占着

        if (_slotLocks.ContainsKey(SlotKey(station.Name, request.Slot)) || _waferLocks.ContainsKey(pickWafer.Id))
        {
            return HandleResult.Fail(ErrorCodes.TransferSlotLocked, station.Name, slotText);
        }

        if (_armLocks.ContainsKey(ArmKey(robot.Name, request.PickArm)) || _slotLocks.ContainsKey(SlotKey(robot.Name, request.PickArm)))
        {
            return HandleResult.Fail(ErrorCodes.TransferArmUnavailable, robot.Name, pickArmText);
        }

        if (_armLocks.ContainsKey(ArmKey(robot.Name, request.PlaceArm)) || _slotLocks.ContainsKey(SlotKey(robot.Name, request.PlaceArm))
            || _waferLocks.ContainsKey(placeWafer.Id))
        {
            return HandleResult.Fail(ErrorCodes.TransferArmUnavailable, robot.Name, placeArmText);
        }

        #endregion

        #region 没在忙：这台机械手、这个站点都没在跑别的搬运

        if (_running.ContainsKey(robot.Name))
        {
            return HandleResult.Fail(ErrorCodes.ActionRejected, robot.Name, robot.State.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var active in _running.Values)
        {
            if (UsesLocation(active, station.Name))
            {
                return HandleResult.Fail(ErrorCodes.ActionRejected, station.Name, station.State.ToString(CultureInfo.InvariantCulture));
            }
        }

        #endregion

        pickName = pickWafer.WaferId;
        placeName = placeWafer.WaferId;
        return null;
    }

    /// <summary>
    /// 这只手在机械手的手指数以内，而且这个站点许用它
    /// </summary>
    private static bool IsArmUsable(int arm, int armCount, RobotStation config)
    {
        return arm >= 1 && arm <= armCount && config.AllowsArm(arm);
    }

    /// <summary>
    /// 片被某个 Job 占着时只能由那个 Job 搬；人工恢复放行
    /// </summary>
    private HandleResult? CheckOwner(Guid waferId, string waferName, string? expected, TransferOrigin origin)
    {
        string? owner = _jobs?.OwnerOf(waferId);
        if (owner is not null && origin != TransferOrigin.Recovery && !string.Equals(owner, expected, StringComparison.Ordinal))
        {
            return HandleResult.Fail(ErrorCodes.TransferWaferOwned, waferName, owner);
        }

        return null;
    }

    #endregion

    #region 中止、恢复、占用查询

    /// <summary>请求中止这个操作，由搬运扫描线程执行设备中止和收尾。</summary>
    public bool Cancel(TransferExecution operation, string reason)
    {
        return Cancel(active => ReferenceEquals(active, operation), reason) > 0;
    }

    /// <summary>
    /// 撤掉还有这个 Job 的片没走完的搬运；交换里它的旧片已经取出来的，让交换把另一片放完（Job 收尾按 HasUnfinishedTransfer 等它）
    /// </summary>
    public int CancelOwner(string owner, string reason)
    {
        return Cancel(operation => operation.HasPendingWork(owner), reason);
    }

    public int CancelAll(string reason)
    {
        return Cancel(_ => true, reason);
    }

    private int Cancel(Func<TransferExecution, bool> match, string reason)
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

    /// <summary>
    /// 人工确认片位及站点状态后，放掉这片所在那条记录保留的全部资源（交换按任意一片都能放整条）
    /// </summary>
    public bool ReleaseHold(Guid waferId)
    {
        TransferExecution? operation;
        lock (_gate)
        {
            if (!_heldByWafer.TryGetValue(waferId, out operation))
            {
                return false;
            }

            _held.Remove(operation);
            foreach (var wafer in operation.Wafers)
            {
                _heldByWafer.Remove(wafer.Id);
            }

            Unlock(operation);
        }

        LogHelper.Info(Name, $"{operation.WaferNames} 的搬运占用已人工放开（片位及站点状态已确认）：{operation.Name}");
        return true;
    }

    /// <summary>运动已开始或站点占用未解除时失败，等待人工确认的操作；一条记录只列一次，已完成的操作不留历史。</summary>
    public IReadOnlyList<TransferExecution> HeldOperations
    {
        get
        {
            lock (_gate)
            {
                return _held.ToList();
            }
        }
    }

    /// <summary>
    /// 这个 Job 还有没收尾的搬运：在跑的、挂着等人工的记录里有它的片
    /// </summary>
    public bool HasUnfinishedTransfer(string owner)
    {
        lock (_gate)
        {
            return _running.Values.Any(operation => operation.InvolvesOwner(owner))
                || _held.Any(operation => operation.InvolvesOwner(owner));
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
        List<TransferExecution> running;
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

    private void Finish(TransferExecution operation)
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
                _held.Add(operation);
                foreach (var wafer in operation.Wafers)
                {
                    _heldByWafer[wafer.Id] = operation;
                }
            }
            else
            {
                Unlock(operation);
            }
        }

        if (operation.IsSuccess)
        {
            LogHelper.Info(Name, $"搬运完成：{operation.WaferNames} {operation.Name}");
        }
        else if (operation.NeedsRecovery)
        {
            LogHelper.Error(Name, $"搬运未完成，运动已开始或站点占用未解除，资源保留到人工确认片位及站点状态：{operation.WaferNames} {operation.Name}（{operation.Reason}）");
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

    /// <summary>
    /// 按这一趟的资源清单一次占上（调用方已在同一把锁里查过都空着）
    /// </summary>
    private void Lock(TransferExecution operation)
    {
        var claims = operation.Claims;
        foreach (string slot in claims.Slots)
        {
            _slotLocks[slot] = operation;
        }

        foreach (string arm in claims.Arms)
        {
            _armLocks[arm] = operation;
        }

        foreach (var wafer in claims.Wafers)
        {
            _waferLocks[wafer] = operation;
        }
    }

    /// <summary>
    /// 按资源清单放掉；只放还归这一趟的
    /// </summary>
    private void Unlock(TransferExecution operation)
    {
        var claims = operation.Claims;
        foreach (string slot in claims.Slots)
        {
            RemoveIfOwned(_slotLocks, slot, operation);
        }

        foreach (string arm in claims.Arms)
        {
            RemoveIfOwned(_armLocks, arm, operation);
        }

        foreach (var wafer in claims.Wafers)
        {
            RemoveIfOwned(_waferLocks, wafer, operation);
        }
    }

    private static void RemoveIfOwned<TKey>(Dictionary<TKey, TransferExecution> locks, TKey key, TransferExecution operation)
        where TKey : notnull
    {
        if (locks.TryGetValue(key, out var holder) && ReferenceEquals(holder, operation))
        {
            locks.Remove(key);
        }
    }

    #endregion
}
