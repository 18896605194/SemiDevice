using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// 调度引擎：从任务表拿任务执行，做完记回任务表。Job 是标准件，派单规则全在平台，机型不另写、不换 Type。
/// </summary>
[Component(description: "调度引擎：从任务表拿任务执行（站内任务交给站点、取放交给搬运管理），做完记回任务表")]
public class SchedulerComponent : ComponentBase, ISchedulerComponent
{
    #region SC

    [SCEditor("False", "Scheduler", "同腔交换：机械手手上的片要进的站里那片做完了，就在站门口先取走旧片、再放进新片，站点少等一来一回；False = 不派交换")]
    public bool EnableSwap { get; set; }

    [SCEditor("False", "Scheduler", "预取：要去的站都满着时，先把下一片取到一只空手上，等站做完就交换；EnableSwap 也开着才生效，多站路线确认不会卡死再打开")]
    public bool EnablePrefetch { get; set; }

    #endregion

    /// <summary>
    /// 搬运管理（开机 Bind 时取）
    /// </summary>
    private ITransferManager? _transfers;
    /// <summary>
    /// 晶圆账（开机 Bind 时取）
    /// </summary>
    private IWaferManagerComponent? _waferManager;

    /// <summary>
    /// 预取意向：站点 → 为它预取到手上的那片；同一个站只预取一片。只在 Job 扫描线程上读写
    /// </summary>
    private readonly Dictionary<string, Guid> _prefetched = new(StringComparer.OrdinalIgnoreCase);

    public override void Bind()
    {
        _transfers = IocHelper.GetService<ITransferManager>();
        _waferManager = IocHelper.GetService<IWaferManagerComponent>();
    }

    /// <summary>
    /// 派这一拍的任务。rows 是有许可的行，按优先级排好（CJ 队列先后、PJ 在 CJ 里的先后、PJ 里的投片顺序）。Manual 下 Job 组件不调。
    /// </summary>
    public void Dispatch(IReadOnlyList<TaskRow> rows, ITaskComponent tasks)
    {
        if (_transfers is null || _waferManager is null)
        {
            return;
        }

        DropStalePrefetches(rows);

        // 先起站内任务：片已经在站点上了
        foreach (var row in rows)
        {
            var task = NextTask(row, TaskPermission.Advance);
            if (task is not null && !task.IsRobotTask)
            {
                StartStationTask(row, task, tasks);
            }
        }

        // 交换：手上的片要去的站里那片做完了，先取后放；排在"走机内的片"前面，免得机械手先去送做完的片
        if (EnableSwap)
        {
            foreach (var row in rows)
            {
                TryStartSwap(row, rows, tasks);
            }
        }

        // 再走机内的片：做完的片占着站点，不挪走后面的片进不来
        foreach (var row in rows)
        {
            var task = NextTask(row, TaskPermission.Advance);
            if (task is not null && task.IsRobotTask && !row.IsWaiting)
            {
                StartTransfer(row, task, tasks);
            }
        }

        // 最后投新片；要去的站都满着时按开关预取
        foreach (var row in rows)
        {
            var task = NextTask(row, TaskPermission.Feed);
            if (task is null || !row.IsWaiting)
            {
                continue;
            }

            if (StartTransfer(row, task, tasks))
            {
                continue;
            }

            if (EnableSwap && EnablePrefetch)
            {
                TryPrefetch(row, task, rows, tasks);
            }
        }
    }

    /// <summary>
    /// 按每行当前的 WaferTask 收执行进度。站内操作收尾后完成这一格；搬运按这一片实际做完的动作记，执行记录只挂在当前任务上。
    /// </summary>
    public void Collect(ITaskComponent tasks)
    {
        foreach (var row in tasks.Rows)
        {
            var task = row.Current;
            var operation = task?.Operation;
            if (task is null || task.State != WaferTaskState.Running || operation is null)
            {
                continue;
            }

            if (operation is TransferExecution transfer)
            {
                CollectTransfer(row, task, transfer, tasks);
            }
            else if (operation.IsSettled)
            {
                if (operation.IsSuccess)
                {
                    tasks.Done(row, task);
                }
                else if (row.IsAborting && operation.State == OperationState.Aborted)
                {
                    tasks.Cancel(row, task);
                }
                else
                {
                    tasks.Fail(row, task, operation.Code, operation.ErrorArgs);
                }
            }
        }
    }

    /// <summary>
    /// 按这一片在这趟执行里实际做完的动作记任务：取完转放片（只取不放的放片留着另派）、放完记完成；
    /// 执行结束这片还没走到下一步的，没动手退回等待，动过手记出错等人工。
    /// </summary>
    private static void CollectTransfer(TaskRow row, WaferTask task, TransferExecution operation, ITaskComponent tasks)
    {
        var wafer = operation.Wafers.FirstOrDefault(item => item.Id == row.WaferId);
        if (wafer is null)
        {
            return;
        }

        if (task.Kind == StationTaskAction.Pick && wafer.Progress != TransferWaferProgress.None)
        {
            // 只取不放（预取、交换换出来的旧片）：片停在手上，放片留着另派，不挂这趟执行
            if (wafer.EndsOnArm)
            {
                tasks.Done(row, task, wafer.Arm);
                return;
            }

            var place = row.NextPlace(task);
            if (place is null)
            {
                return;
            }

            tasks.Done(row, task, wafer.Arm);
            tasks.Start(row, place, wafer.To, wafer.ToSlot, operation.Robot.Name, operation);
            task = place;
        }

        if (task.Kind == StationTaskAction.Place && wafer.Progress == TransferWaferProgress.Placed)
        {
            tasks.Done(row, task, wafer.Arm);
            return;
        }

        if (!operation.IsSettled)
        {
            return;
        }

        if (operation.IsSuccess)
        {
            tasks.Done(row, task, wafer.Arm);
            return;
        }

        if (!operation.NeedsRecovery)
        {
            tasks.Reset(task);
        }
        else
        {
            tasks.Fail(row, task, operation.Code, operation.ErrorArgs);
        }
    }

    /// <summary>
    /// 这一行轮到的、等着做的任务（有这个许可才算）；不该派返回 null
    /// </summary>
    private static WaferTask? NextTask(TaskRow row, TaskPermission needed)
    {
        var current = row.Current;
        if (current is null || current.State != WaferTaskState.Waiting || (row.Permission & needed) == 0)
        {
            return null;
        }

        return current;
    }

    #region 启动任务

    /// <summary>
    /// 启动站点内的任务
    /// </summary>
    private void StartStationTask(TaskRow row, WaferTask task, ITaskComponent tasks)
    {
        var location = row.ExpectedLocation;
        ITransferStation? station = null;
        if (!location.IsArm)
        {
            station = Station(location.Module);
        }

        if (station is null || !IsOnline(station) || station is not IStationTaskExecutor executor)
        {
            return;
        }

        var operation = executor.StartTask(new StationTaskRequest
        {
            Kind = task.Kind,
            Owner = row.Owner,
            WaferId = row.WaferId,
            Slot = location.Slot,
            Step = task.Step,
            RecipeName = task.RecipeName,
            Recipe = task.Recipe,
        });
        if (operation is not null)
        {
            tasks.Start(row, task, location.Module, location.Slot, operation: operation);
        }
    }

    /// <summary>
    /// 启动传输
    /// </summary>
    /// <param name="row"></param>
    /// <param name="task"></param>
    /// <param name="tasks"></param>
    /// <returns></returns>
    private bool StartTransfer(TaskRow row, WaferTask task, ITaskComponent tasks)
    {
        var source = row.ExpectedLocation;
        WaferTask? pick = null;
        WaferTask? place = task;
        if (task.Kind == StationTaskAction.Pick)
        {
            pick = task;
            place = row.NextPlace(task);
        }

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
            return IsStationReady(port) && IsSlotFree(port, slot)
                && StartTransfer(row, task, source, port, slot, tasks);
        }

        // 站点组按流程配方里勾的先后挑，第一个能放的就用
        foreach (string station in place.Stations)
        {
            if (!IsStationReady(station))
            {
                continue;
            }

            int slot = FreeSlot(station);
            if (slot > 0 && StartTransfer(row, task, source, station, slot, tasks))
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region 同腔交换与预取

    /// <summary>
    /// 交换：这一片在机械手手上等着放，它要去的站组里有站的片做完了，就在那个站先取走旧片、再放进这一片
    /// </summary>
    private void TryStartSwap(TaskRow row, IReadOnlyList<TaskRow> rows, ITaskComponent tasks)
    {
        var place = NextTask(row, TaskPermission.Advance);
        var at = row.ExpectedLocation;
        if (place is null || place.Kind != StationTaskAction.Place || !at.IsArm || place.FixedSlot > 0)
        {
            return;
        }

        var robot = Robot(at.Module);
        if (robot is null || !IsRobotFree(robot))
        {
            return;
        }

        foreach (string station in place.Stations)
        {
            if (TryStartSwapAt(row, place, robot, at.Slot, station, rows, tasks))
            {
                return;
            }
        }
    }

    /// <summary>
    /// 在这个站找一片做完、能换出来的旧片，挑一只空手给它，提交交换；两片的任务都挂上这趟交换
    /// </summary>
    private bool TryStartSwapAt(TaskRow row, WaferTask place, ITransferRobot robot, int placeArm, string stationName,
        IReadOnlyList<TaskRow> rows, ITaskComponent tasks)
    {
        var station = Station(stationName);
        if (station is null || !IsStationReady(stationName) || !robot.TryGetStation(stationName, out var config)
            || !config.AllowsArm(placeArm))
        {
            return false;
        }

        for (int slot = 1; slot <= station.SlotCount; slot++)
        {
            var finished = FinishedRow(stationName, slot, rows);
            if (finished is null || _transfers!.IsSlotLocked(stationName, slot))
            {
                continue;
            }

            var pick = NextTask(finished, TaskPermission.Advance);
            if (pick is null)
            {
                continue;
            }

            int pickArm = FindPickArm(robot, finished, pick, config, placeArm);
            if (pickArm == 0)
            {
                continue;
            }

            var started = _transfers.StartSwap(new SwapRequest
            {
                Origin = TransferOrigin.Auto,
                Robot = robot.Name,
                Station = stationName,
                Slot = slot,
                PickArm = pickArm,
                PlaceArm = placeArm,
                PickWafer = finished.WaferId,
                PlaceWafer = row.WaferId,
                PickOwner = finished.Owner,
                PlaceOwner = row.Owner,
            });
            var operation = started.Result;
            if (!started.IsSuccess || operation is null)
            {
                continue;
            }

            tasks.Start(finished, pick, stationName, slot, robot.Name, operation);
            tasks.Start(row, place, stationName, slot, robot.Name, operation);
            _prefetched.Remove(stationName);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 这个槽里那片做完了、轮到取片的那一行（片按晶圆账认）；没有返回 null
    /// </summary>
    private TaskRow? FinishedRow(string station, int slot, IReadOnlyList<TaskRow> rows)
    {
        var wafer = _waferManager!.Get(station, slot);
        if (wafer is null)
        {
            return null;
        }

        var row = rows.FirstOrDefault(item => item.WaferId == wafer.Id);
        if (row is null)
        {
            return null;
        }

        var pick = NextTask(row, TaskPermission.Advance);
        if (pick is null || pick.Kind != StationTaskAction.Pick)
        {
            return null;
        }

        return row;
    }

    /// <summary>
    /// 预取：新片要去的站都满着，组里有站快做完了，就先把新片只取到一只空手上，另留一只空手给换出来的旧片
    /// </summary>
    private void TryPrefetch(TaskRow row, WaferTask pick, IReadOnlyList<TaskRow> rows, ITaskComponent tasks)
    {
        var source = row.ExpectedLocation;
        var place = row.NextPlace(pick);
        if (source.IsArm || place is null || place.FixedSlot > 0 || !IsStationReady(source.Module))
        {
            return;
        }

        foreach (var robot in _transfers!.Robots)
        {
            if (!IsRobotFree(robot) || !robot.TryGetStation(source.Module, out var from))
            {
                continue;
            }

            foreach (string stationName in place.Stations)
            {
                if (TryPrefetchFor(row, pick, robot, from, source, stationName, rows, tasks))
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 为这个站预取：站里那片正在做或做完了、换出来有去处，机械手有一只手能从来源取、往这个站放，另有一只空手能取旧片
    /// </summary>
    private bool TryPrefetchFor(TaskRow row, WaferTask pick, ITransferRobot robot, RobotStation from,
        (string Module, int Slot, bool IsArm) source, string stationName, IReadOnlyList<TaskRow> rows, ITaskComponent tasks)
    {
        if (_prefetched.ContainsKey(stationName) || !robot.TryGetStation(stationName, out var at))
        {
            return false;
        }

        var finishing = FinishingRow(stationName, rows, out var finishingPick);
        if (finishing is null || finishingPick is null)
        {
            return false;
        }

        int armCount = _waferManager!.GetSlots(robot.Name).Count;
        for (int carryArm = 1; carryArm <= armCount; carryArm++)
        {
            if (!from.AllowsArm(carryArm) || !at.AllowsArm(carryArm) || !IsArmFree(robot, carryArm)
                || FindPickArm(robot, finishing, finishingPick, at, carryArm) == 0)
            {
                continue;
            }

            var started = _transfers!.Start(new TransferRequest
            {
                Origin = TransferOrigin.Auto,
                Owner = row.Owner,
                WaferId = row.WaferId,
                Source = source.Module,
                SourceSlot = source.Slot,
                Target = robot.Name,
                TargetSlot = carryArm,
                Robot = robot.Name,
                Arm = carryArm,
            });
            var operation = started.Result;
            if (!started.IsSuccess || operation is null)
            {
                continue;
            }

            tasks.Start(row, pick, source.Module, source.Slot, robot.Name, operation);
            _prefetched[stationName] = row.WaferId;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 这个站里正在做工艺、或者做完等取的那一行，以及它的取片任务；站点停用、下线、出错的不算
    /// </summary>
    private TaskRow? FinishingRow(string stationName, IReadOnlyList<TaskRow> rows, out WaferTask? pick)
    {
        pick = null;
        var station = Station(stationName);
        if (station is null || !station.IsEnabled || !IsOnline(station) || station.State == ModuleState.Error)
        {
            return null;
        }

        for (int slot = 1; slot <= station.SlotCount; slot++)
        {
            var wafer = _waferManager!.Get(stationName, slot);
            if (wafer is null)
            {
                continue;
            }

            var row = rows.FirstOrDefault(item => item.WaferId == wafer.Id);
            var current = row?.Current;
            if (row is null || current is null)
            {
                continue;
            }

            if (current.Kind == StationTaskAction.Pick && current.State == WaferTaskState.Waiting)
            {
                pick = current;
                return row;
            }

            if (current.Kind == StationTaskAction.Process && current.State == WaferTaskState.Running)
            {
                int index = row.Tasks.IndexOf(current);
                pick = row.Tasks.Skip(index + 1).FirstOrDefault(task => task.Kind == StationTaskAction.Pick);
                if (pick is not null)
                {
                    return row;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 给换出来的旧片挑一只空手：这个站许用、空着，拿着旧片的这只手到得了它的下一站、那里收得下；没有返回 0
    /// </summary>
    private int FindPickArm(ITransferRobot robot, TaskRow row, WaferTask pick, RobotStation at, int excludeArm)
    {
        var next = row.NextPlace(pick);
        if (next is null)
        {
            return 0;
        }

        int armCount = _waferManager!.GetSlots(robot.Name).Count;
        for (int arm = 1; arm <= armCount; arm++)
        {
            if (arm == excludeArm || !at.AllowsArm(arm) || !IsArmFree(robot, arm))
            {
                continue;
            }

            if (HasPlaceFor(robot, arm, next))
            {
                return arm;
            }
        }

        return 0;
    }

    /// <summary>
    /// 这只手拿着片能完成这个放片任务：机械手到得了、这只手许用、站点待命、槽空着（回片看定好的那一槽）
    /// </summary>
    private bool HasPlaceFor(ITransferRobot robot, int arm, WaferTask place)
    {
        if (place.FixedSlot > 0 && place.Stations.Count > 0)
        {
            string port = place.Stations[0];
            return CanArmServe(robot, arm, port) && IsStationReady(port) && IsSlotFree(port, place.FixedSlot);
        }

        foreach (string station in place.Stations)
        {
            if (CanArmServe(robot, arm, station) && IsStationReady(station) && FreeSlot(station) > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 作废过时的预取意向：开关关了、那片已经不在等这个站（放到别处、没取成）、站点停用下线出错、站里没有快做完的片
    /// </summary>
    private void DropStalePrefetches(IReadOnlyList<TaskRow> rows)
    {
        if (_prefetched.Count == 0)
        {
            return;
        }

        if (!EnableSwap || !EnablePrefetch)
        {
            _prefetched.Clear();
            return;
        }

        foreach (var entry in _prefetched.ToList())
        {
            if (!IsWaitingForSwap(entry.Value, rows) || FinishingRow(entry.Key, rows, out _) is null)
            {
                _prefetched.Remove(entry.Key);
            }
        }
    }

    /// <summary>
    /// 预取的这片还在等交换：预取那一趟还在取，或者已经在手上等着放
    /// </summary>
    private static bool IsWaitingForSwap(Guid waferId, IReadOnlyList<TaskRow> rows)
    {
        var row = rows.FirstOrDefault(item => item.WaferId == waferId);
        var current = row?.Current;
        if (row is null || current is null)
        {
            return false;
        }

        if (current.Kind == StationTaskAction.Pick && current.State == WaferTaskState.Running)
        {
            return true;
        }

        return current.Kind == StationTaskAction.Place && current.State == WaferTaskState.Waiting && row.ExpectedLocation.IsArm;
    }

    #endregion

    #region 设备与资源占用查询

    /// <summary>
    /// 按名字找站点（搬运管理的模块表）；不是站点为 null
    /// </summary>
    private ITransferStation? Station(string name)
    {
        var transfers = _transfers;
        if (transfers is not null && transfers.TryGetStation(name, out var station))
        {
            return station;
        }

        return null;
    }

    /// <summary>
    /// 按名字找机械手（搬运管理的模块表）；不是机械手为 null
    /// </summary>
    private ITransferRobot? Robot(string name)
    {
        var transfers = _transfers;
        if (transfers is not null && transfers.TryGetRobot(name, out var robot))
        {
            return robot;
        }

        return null;
    }

    /// <summary>
    /// 站点现在能不能服务机械手：装了、启用、在线、在待命、没在做别的动作。
    /// </summary>
    private bool IsStationReady(string name)
    {
        var station = Station(name);
        if (station is null || !station.IsEnabled || !IsOnline(station) || !station.CanPrepareTransfer)
        {
            return false;
        }

        var current = station.CurrentOperation;
        return current is null || current.IsTerminal;
    }

    /// <summary>
    /// 调度用到的站点（LoadPort、腔体……）和机械手都得在线：下线的不派新任务，已经在做的照常做完；上线了下一拍接着派。
    /// 手动页、手动取放不看这个，下线就是留给人手动操作的。
    /// </summary>
    private static bool IsOnline(IModule module)
    {
        return module.Mode == ModuleMode.Online;
    }

    /// <summary>
    /// 这一槽账上空着、也没被搬运操作占用
    /// </summary>
    private bool IsSlotFree(string station, int slot)
    {
        return _waferManager is not null && _transfers is not null
            && _waferManager.Get(station, slot) is null && !_transfers.IsSlotLocked(station, slot);
    }

    /// <summary>
    /// 这只手账上空着、也没被搬运操作当作位置占用
    /// </summary>
    private bool IsArmFree(ITransferRobot robot, int arm)
    {
        return IsSlotFree(robot.Name, arm);
    }

    /// <summary>
    /// 机械手到得了这个站，而且这个站许用这只手
    /// </summary>
    private static bool CanArmServe(ITransferRobot robot, int arm, string station)
    {
        return robot.TryGetStation(station, out var config) && config.AllowsArm(arm);
    }

    /// <summary>
    /// 站点上第一个空着、没被占用的槽；没有返回 0
    /// </summary>
    private int FreeSlot(string station)
    {
        int count = Station(station)?.SlotCount ?? 0;
        for (int slot = 1; slot <= count; slot++)
        {
            if (IsSlotFree(station, slot))
            {
                return slot;
            }
        }

        return 0;
    }

    /// <summary>
    /// 机械手可执行任务：启用、在线、空闲、没有搬运操作
    /// </summary>
    private bool IsRobotFree(ITransferRobot robot)
    {
        if (_transfers?.IsRobotInTransfer(robot.Name) == true || robot.State != ModuleState.Idle)
        {
            return false;
        }

        return robot.IsEnabled && IsOnline(robot);
    }

    #endregion

    /// <summary>
    /// 找机械手、启动实际搬运操作并挂到当前任务上；取片确认后才进入放片任务。
    /// 目标槽和机械手在启动时一起占住，避免取片后没有位置可放。
    /// 只放片（片在机械手手上）就用拿着片的那台。
    /// </summary>
    private bool StartTransfer(TaskRow row, WaferTask task, (string Module, int Slot, bool IsArm) source, string target, int targetSlot, ITaskComponent tasks)
    {
        var transfers = _transfers!;
        foreach (var robot in transfers.Robots)
        {
            if (!IsRobotFree(robot) || !robot.TryGetStation(target, out _))
            {
                continue;
            }

            if (!IsSourceReachable(robot, source))
            {
                continue;
            }

            // 手臂选择、片位与资源校验统一由搬运执行口做；当前机械手不能接就尝试下一台。
            var started = transfers.Start(new TransferRequest
            {
                Origin = TransferOrigin.Auto,
                Owner = row.Owner,
                WaferId = row.WaferId,
                Source = source.Module,
                SourceSlot = source.Slot,
                Target = target,
                TargetSlot = targetSlot,
                Robot = robot.Name,
            });
            var operation = started.Result;
            if (!started.IsSuccess || operation is null)
            {
                continue;
            }

            if (task.Kind == StationTaskAction.Pick)
            {
                tasks.Start(row, task, source.Module, source.Slot, robot.Name, operation);
            }
            else
            {
                tasks.Start(row, task, target, targetSlot, robot.Name, operation);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 这台机械手能接这片：片在手上要是它自己的手，片在站点上要到得了那个站
    /// </summary>
    private static bool IsSourceReachable(ITransferRobot robot, (string Module, int Slot, bool IsArm) source)
    {
        if (source.IsArm)
        {
            return string.Equals(robot.Name, source.Module, StringComparison.OrdinalIgnoreCase);
        }

        return robot.TryGetStation(source.Module, out _);
    }
}
