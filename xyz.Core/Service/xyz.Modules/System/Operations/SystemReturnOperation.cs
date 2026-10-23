using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 全部回片：机内的片照晶圆账记的走过的位置倒着走，回各自来源 LoadPort 的原槽，一次只搬一趟
/// </summary>
internal sealed class SystemReturnOperation : ModuleOperation
{
    /// <summary>
    /// 回片表的一行：一片现在在哪、还要经过哪些站
    /// </summary>
    private sealed class ReturnWafer
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        /// <summary>
        /// 现在在哪：站点名，或者拿着它的机械手名
        /// </summary>
        public required string Module { get; set; }

        /// <summary>
        /// 现在在哪一槽（在机械手上是手指号）
        /// </summary>
        public required int Slot { get; set; }

        /// <summary>
        /// 还要经过的站，按先后；最后一站是来源 LoadPort
        /// </summary>
        public required List<string> Stops { get; init; }

        /// <summary>
        /// 回来源 LoadPort 的哪一槽
        /// </summary>
        public required int ReturnSlot { get; init; }
    }

    /// <summary>
    /// 日志记在系统模块名下
    /// </summary>
    private readonly string _logModule;

    private readonly ITransferManager _transfers;

    private readonly ILedgerManager _ledger;

    /// <summary>
    /// 还没回去的片
    /// </summary>
    private readonly List<ReturnWafer> _wafers;

    /// <summary>
    /// 机械手、站点在 sc 里的先后，挑片时同一类按它排
    /// </summary>
    private readonly Dictionary<string, int> _order = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 在搬的那一趟；没在搬为 null
    /// </summary>
    private TransferRoutine? _routine;

    /// <summary>
    /// 在搬的那一片
    /// </summary>
    private ReturnWafer? _moving;

    private SystemReturnOperation(string logModule, ITransferManager transfers, ILedgerManager ledger, List<ReturnWafer> wafers)
        : base("ReturnAll")
    {
        _logModule = logModule;
        _transfers = transfers;
        _ledger = ledger;
        _wafers = wafers;
        foreach (var robot in transfers.Robots)
        {
            _order.TryAdd(robot.Name, _order.Count);
        }

        foreach (var station in transfers.Stations)
        {
            _order.TryAdd(station.Name, _order.Count);
        }
    }

    #region 开始前：检查、排表

    /// <summary>
    /// 开始前把机内每片的去处、路线、要用到的设备、在位传感器都查一遍：有一条不过就不开始、回原因，都过了给回片动作
    /// </summary>
    public static HandleResult<ModuleOperation> Prepare(string logModule, ITransferManager transfers, ILedgerManager ledger, IJobManager? jobs)
    {
        if (jobs is not null && jobs.HasRunningJob)
        {
            return HandleResult<ModuleOperation>.Fail(ErrorCodes.SystemReturnJobRunning);
        }

        var wafers = new List<ReturnWafer>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var wafer in WafersInMachine(transfers, ledger))
        {
            var rejected = Plan(transfers, ledger, wafer, claimed, out var entry);
            if (rejected is not null)
            {
                return Refused(rejected);
            }

            wafers.Add(entry);
        }

        var notReady = CheckModules(transfers, ledger, wafers) ?? CheckSensors(transfers);
        if (notReady is not null)
        {
            return Refused(notReady);
        }

        LogHelper.Info(logModule, $"全部回片：机内 {wafers.Count} 片，检查都过了");
        return HandleResult<ModuleOperation>.Success(new SystemReturnOperation(logModule, transfers, ledger, wafers));
    }

    /// <summary>
    /// 机内的片：机械手手上的、LoadPort 以外的站点上的
    /// </summary>
    private static List<WaferInfo> WafersInMachine(ITransferManager transfers, ILedgerManager ledger)
    {
        var wafers = new List<WaferInfo>();
        foreach (var robot in transfers.Robots)
        {
            foreach (var wafer in ledger.GetSlots(robot.Name))
            {
                if (wafer is not null)
                {
                    wafers.Add(wafer);
                }
            }
        }

        foreach (var station in transfers.Stations)
        {
            if (station is ILoadPort)
            {
                continue;
            }

            foreach (var wafer in ledger.GetSlots(station.Name))
            {
                if (wafer is not null)
                {
                    wafers.Add(wafer);
                }
            }
        }

        return wafers;
    }

    /// <summary>
    /// 排一片：查去处（来源口、载具、原槽），排倒着走的站，查每一段都有机械手够得着；不过回原因
    /// </summary>
    private static HandleResult? Plan(ITransferManager transfers, ILedgerManager ledger, WaferInfo wafer, HashSet<string> claimed,
        out ReturnWafer entry)
    {
        entry = null!;
        string location = string.Create(CultureInfo.InvariantCulture, $"{wafer.Module}.{wafer.Slot:00}");
        string? port = wafer.SourceLoadPort;
        int slot = wafer.SourceSlot;
        if (string.IsNullOrEmpty(port) || slot < 1 || !transfers.TryGetStation(port, out var station)
            || station is not ILoadPort loadPort || slot > station.SlotCount)
        {
            return HandleResult.Fail(ErrorCodes.SystemReturnNoSource, wafer.WaferId, location);
        }

        var carrier = loadPort._carrier;
        if (!carrier.IsArrived || !loadPort.IsLoaded)
        {
            return HandleResult.Fail(ErrorCodes.SystemReturnCarrierNotLoaded, wafer.WaferId, port);
        }

        // 载具换过了：片记着的载具号跟口上现在的对不上，不能往别人的载具里放（有一边没读到号就不比）
        string? expected = wafer.CarrierId;
        string? actual = carrier.CarrierId;
        if (!string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(actual)
            && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return HandleResult.Fail(ErrorCodes.SystemReturnCarrierChanged, wafer.WaferId, port, expected, actual);
        }

        if (ledger.Get(port, slot) is not null || !claimed.Add(TransferManager.SlotKey(port, slot)))
        {
            return HandleResult.Fail(ErrorCodes.SystemReturnSlotOccupied, wafer.WaferId, port, slot.ToString(CultureInfo.InvariantCulture));
        }

        var stops = ReturnStops(transfers, wafer);
        stops.Add(port);
        string from = wafer.Module;
        int heldArm = HeldArm(transfers, wafer.Module, wafer.Slot);
        foreach (string stop in stops)
        {
            if (FindRobot(transfers, ledger, from, heldArm, stop, now: false) is null)
            {
                return HandleResult.Fail(ErrorCodes.SystemReturnUnreachable, wafer.WaferId, from, stop);
            }

            from = stop;
            heldArm = 0;
        }

        entry = new ReturnWafer
        {
            Id = wafer.Id,
            Name = wafer.WaferId,
            Module = wafer.Module,
            Slot = wafer.Slot,
            Stops = stops,
            ReturnSlot = slot,
        };
        return null;
    }

    /// <summary>
    /// 倒着走要经过的站：走过的位置倒过来，不算机械手、片现在待的那一站、能做工艺的站（片在手上时，刚取出来的那一站要放回去）
    /// </summary>
    private static List<string> ReturnStops(ITransferManager transfers, WaferInfo wafer)
    {
        var stops = new List<string>();
        for (int index = wafer.Path.Count - 1; index >= 0; index--)
        {
            string name = wafer.Path[index];
            if (!transfers.TryGetStation(name, out var station) || station is IProcessStation
                || string.Equals(name, wafer.Module, StringComparison.OrdinalIgnoreCase) || stops.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            stops.Add(name);
        }

        return stops;
    }

    /// <summary>
    /// 要用到的机械手、站点现在都能用：站点启用、能准备取放；机械手启用、空闲、没在搬
    /// </summary>
    private static HandleResult? CheckModules(ITransferManager transfers, ILedgerManager ledger, List<ReturnWafer> wafers)
    {
        var robots = new List<ITransferRobot>();
        var stations = new List<string>();
        foreach (var wafer in wafers)
        {
            string from = wafer.Module;
            int heldArm = HeldArm(transfers, wafer.Module, wafer.Slot);
            if (heldArm == 0 && !stations.Contains(from, StringComparer.OrdinalIgnoreCase))
            {
                stations.Add(from);
            }

            foreach (string stop in wafer.Stops)
            {
                if (!stations.Contains(stop, StringComparer.OrdinalIgnoreCase))
                {
                    stations.Add(stop);
                }

                var used = FindRobot(transfers, ledger, from, heldArm, stop, now: false);
                if (used is not null && !robots.Contains(used.Value.Robot))
                {
                    robots.Add(used.Value.Robot);
                }

                from = stop;
                heldArm = 0;
            }
        }

        foreach (var robot in robots)
        {
            if (!IsRobotReady(transfers, robot))
            {
                return HandleResult.Fail(ErrorCodes.SystemReturnModuleNotReady, robot.Name);
            }
        }

        foreach (string name in stations)
        {
            if (!transfers.TryGetStation(name, out var station) || !IsStationReady(station))
            {
                return HandleResult.Fail(ErrorCodes.SystemReturnModuleNotReady, name);
            }
        }

        return null;
    }

    /// <summary>
    /// 启用的机械手、站点：sc 配了在位传感器的都跟晶圆账对一遍
    /// </summary>
    private static HandleResult? CheckSensors(ITransferManager transfers)
    {
        foreach (var robot in transfers.Robots)
        {
            if (robot.IsEnabled && robot is IRobot sensing)
            {
                var mismatch = sensing.CheckWaferSensors();
                if (mismatch is not null)
                {
                    return mismatch;
                }
            }
        }

        foreach (var station in transfers.Stations)
        {
            if (!station.IsEnabled)
            {
                continue;
            }

            var mismatch = station.CheckWaferSensors();
            if (mismatch is not null)
            {
                return mismatch;
            }
        }

        return null;
    }

    /// <summary>
    /// 检查没过：把原因原样转成全部回片入口的结果
    /// </summary>
    private static HandleResult<ModuleOperation> Refused(HandleResult reason)
    {
        return HandleResult<ModuleOperation>.Fail(reason.ErrorMessage, [.. reason.Args]);
    }

    #endregion

    #region 一趟一趟搬

    /// <summary>
    /// 每拍：收在搬的那一趟；没在搬的按先后挑第一片能动的下一趟；都动不了、也没有设备在做动作就停下
    /// </summary>
    protected override void OnScan()
    {
        if (!Collect())
        {
            return;
        }

        if (_wafers.Count == 0)
        {
            LogHelper.Info(_logModule, "全部回片：片都回去了");
            Complete();
            return;
        }

        foreach (var wafer in Ordered())
        {
            if (TryStart(wafer) || IsTerminal)
            {
                return;
            }
        }

        if (!AnyBusy())
        {
            GiveUp("剩下的片都动不了（要去的槽满着、或者设备不在待命），也没有设备在做动作");
        }
    }

    /// <summary>
    /// 收在搬的那一趟：没收完尾返回 false；做成了这片前进一站，到了 LoadPort 就划掉；没做成整个停
    /// </summary>
    private bool Collect()
    {
        var routine = _routine;
        var wafer = _moving;
        if (routine is null || wafer is null)
        {
            return true;
        }

        if (!routine.IsSettled)
        {
            return false;
        }

        _routine = null;
        _moving = null;
        if (!routine.IsSuccess)
        {
            GiveUp($"{wafer.Name} 从 {wafer.Module}.{wafer.Slot:00} 去 {wafer.Stops[0]} 没搬成：{routine.Reason}");
            return false;
        }

        wafer.Module = routine.TargetName;
        wafer.Slot = routine.TargetSlot;
        wafer.Stops.RemoveAt(0);
        if (wafer.Stops.Count == 0)
        {
            _wafers.Remove(wafer);
            LogHelper.Info(_logModule, $"全部回片：{wafer.Name} 已回 {wafer.Module} {wafer.Slot}");
        }

        return true;
    }

    /// <summary>
    /// 挑片的先后：机械手手上的 → 其余站点上的 → 能做工艺的站点上的；同一类按 sc 先后、槽号
    /// </summary>
    private List<ReturnWafer> Ordered()
    {
        return _wafers.OrderBy(Rank).ThenBy(wafer => OrderOf(wafer.Module)).ThenBy(wafer => wafer.Slot).ToList();
    }

    private int Rank(ReturnWafer wafer)
    {
        if (_transfers.TryGetRobot(wafer.Module, out _))
        {
            return 0;
        }

        if (_transfers.TryGetStation(wafer.Module, out var station) && station is IProcessStation)
        {
            return 2;
        }

        return 1;
    }

    private int OrderOf(string module)
    {
        if (_order.TryGetValue(module, out int index))
        {
            return index;
        }

        return int.MaxValue;
    }

    /// <summary>
    /// 这片现在能动就下这一趟：片还在记的位置、下一站有空槽、两头站点在待命、有空闲的机械手和手；
    /// 动不了返回 false 看下一片，片被人挪过或者搬运被拒就整个停
    /// </summary>
    private bool TryStart(ReturnWafer wafer)
    {
        var found = _ledger.FindById(wafer.Id);
        if (found is null || !string.Equals(found.Module, wafer.Module, StringComparison.OrdinalIgnoreCase) || found.Slot != wafer.Slot)
        {
            GiveUp($"{wafer.Name} 不在 {wafer.Module}.{wafer.Slot:00} 了（被人挪过或删了账）");
            return false;
        }

        string next = wafer.Stops[0];
        if (!_transfers.TryGetStation(next, out var target) || !IsStationReady(target))
        {
            return false;
        }

        int targetSlot = wafer.ReturnSlot;
        if (wafer.Stops.Count > 1)
        {
            targetSlot = FreeSlot(target);
        }

        if (targetSlot == 0 || !IsSlotFree(next, targetSlot))
        {
            return false;
        }

        int heldArm = HeldArm(_transfers, wafer.Module, wafer.Slot);
        if (heldArm == 0 && (!_transfers.TryGetStation(wafer.Module, out var source) || !IsStationReady(source)))
        {
            return false;
        }

        var robot = FindRobot(_transfers, _ledger, wafer.Module, heldArm, next, now: true);
        if (robot is null)
        {
            return false;
        }

        var started = _transfers.Start(new TransferRequest
        {
            Origin = TransferOrigin.Recovery,
            WaferId = wafer.Id,
            Source = wafer.Module,
            SourceSlot = wafer.Slot,
            Target = next,
            TargetSlot = targetSlot,
            Robot = robot.Value.Robot.Name,
            Arm = robot.Value.Arm,
        });
        var routine = started.Result;
        if (!started.IsSuccess || routine is null)
        {
            GiveUp($"{wafer.Name} 从 {wafer.Module}.{wafer.Slot:00} 去 {next} {targetSlot} 的搬运被拒：{started.ErrorMessage} [{string.Join(", ", started.Args)}]");
            return false;
        }

        _routine = routine;
        _moving = wafer;
        return true;
    }

    /// <summary>
    /// 中间站挑第一个空着、没被占的槽；没有返回 0
    /// </summary>
    private int FreeSlot(ITransferStation station)
    {
        for (int slot = 1; slot <= station.SlotCount; slot++)
        {
            if (IsSlotFree(station.Name, slot))
            {
                return slot;
            }
        }

        return 0;
    }

    private bool IsSlotFree(string station, int slot)
    {
        return _ledger.Get(station, slot) is null && !_transfers.IsSlotLocked(station, slot);
    }

    /// <summary>
    /// 有没有设备正在做动作（对准器在对准、机械手在做别的）：有就等，别当成卡住
    /// </summary>
    private bool AnyBusy()
    {
        foreach (var robot in _transfers.Robots)
        {
            if (IsBusy(robot) || _transfers.IsRobotInTransfer(robot.Name))
            {
                return true;
            }
        }

        foreach (var wafer in _wafers)
        {
            if (_transfers.TryGetStation(wafer.Module, out var at) && IsBusy(at))
            {
                return true;
            }

            if (_transfers.TryGetStation(wafer.Stops[0], out var next) && IsBusy(next))
            {
                return true;
            }
        }

        return false;
    }

    private void GiveUp(string reason)
    {
        LogHelper.Warn(_logModule, $"全部回片停下：{reason}；还有 {_wafers.Count} 片没回去");
        Fail(ErrorCodes.SystemReturnIncomplete, reason, _wafers.Count.ToString(CultureInfo.InvariantCulture));
    }

    #endregion

    #region 设备查询（开始前、搬的时候共用）

    /// <summary>
    /// 片在机械手手上时是哪只手；在站点上为 0
    /// </summary>
    private static int HeldArm(ITransferManager transfers, string module, int slot)
    {
        if (transfers.TryGetRobot(module, out _))
        {
            return slot;
        }

        return 0;
    }

    /// <summary>
    /// 挑能搬这一段的机械手和手：片在手上就是拿着它的那只手（要许它往目标放）；片在站点上按 sc 先后挑第一台两头都到得了、
    /// 有一只手两头都许用的。now = 现在就要搬：机械手还要空闲、那只手账上空着没被占
    /// </summary>
    private static (ITransferRobot Robot, int Arm)? FindRobot(ITransferManager transfers, ILedgerManager ledger,
        string from, int heldArm, string to, bool now)
    {
        if (heldArm > 0)
        {
            if (transfers.TryGetRobot(from, out var holder) && holder.TryGetStation(to, out var place) && place.AllowsPlace(heldArm)
                && (!now || IsRobotReady(transfers, holder)))
            {
                return (holder, heldArm);
            }

            return null;
        }

        foreach (var robot in transfers.Robots)
        {
            if (!robot.TryGetStation(from, out var source) || !robot.TryGetStation(to, out var target)
                || (now && !IsRobotReady(transfers, robot)))
            {
                continue;
            }

            int armCount = ledger.GetSlots(robot.Name).Count;
            for (int arm = 1; arm <= armCount; arm++)
            {
                if (!source.AllowsPick(arm) || !target.AllowsPlace(arm))
                {
                    continue;
                }

                if (now && (ledger.Get(robot.Name, arm) is not null || transfers.IsSlotLocked(robot.Name, arm)))
                {
                    continue;
                }

                return (robot, arm);
            }
        }

        return null;
    }

    /// <summary>
    /// 机械手现在能接活：启用、空闲、没有没收尾的动作、没在搬
    /// </summary>
    private static bool IsRobotReady(ITransferManager transfers, ITransferRobot robot)
    {
        return robot.IsEnabled && robot.State == ModuleState.Idle && !IsBusy(robot) && !transfers.IsRobotInTransfer(robot.Name);
    }

    /// <summary>
    /// 站点现在能服务机械手：启用、在待命、没在做别的
    /// </summary>
    private static bool IsStationReady(ITransferStation station)
    {
        return station.IsEnabled && station.CheckPrepareTransfer() is null;
    }

    /// <summary>
    /// 模块手上有没收完尾的动作
    /// </summary>
    private static bool IsBusy(IModule module)
    {
        var current = module.CurrentOperation;
        return current is not null && !current.IsSettled;
    }

    #endregion
}
