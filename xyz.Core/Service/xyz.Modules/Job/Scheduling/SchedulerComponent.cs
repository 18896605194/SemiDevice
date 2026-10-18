using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
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
    /// <summary>
    /// 搬运管理（开机 Bind 时取）
    /// </summary>
    private ITransferManager? _transfers;
    /// <summary>
    /// 晶圆账（开机 Bind 时取）
    /// </summary>
    private IWaferManagerComponent? _waferManager;

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
                StartTransfer(row, task, tasks);
            }
        }

        // 最后投新片
        foreach (var row in rows)
        {
            var task = NextTask(row, TaskPermission.Feed);
            if (task is null || !row.IsWaiting)
            {
                continue;
            }

            StartTransfer(row, task, tasks);
        }
    }

    /// <summary>
    /// 按每行当前的 WaferTask 收执行进度。取片确认后转到放片任务，沿用已占好目标的搬运操作；
    /// 站内操作收尾后完成这一格。执行记录只挂在当前任务上。
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

            if (operation is TransferRoutine transfer)
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

    /// <summary>取片后开始放片格；没动手就失败退回等待，动过手才失败则当前格记出错。</summary>
    private static void CollectTransfer(TaskRow row, WaferTask task, TransferRoutine operation, ITaskComponent tasks)
    {
        if (task.Kind == StationTaskAction.Pick && operation.HasPicked)
        {
            var place = row.NextPlace(task);
            if (place is null)
            {
                return;
            }

            tasks.Done(row, task, operation.Arm);
            tasks.Start(row, place, operation.TargetName, operation.TargetSlot, operation.Robot.Name, operation);
            task = place;
        }

        if (!operation.IsSettled)
        {
            return;
        }

        if (operation.IsSuccess)
        {
            tasks.Done(row, task, operation.Arm);
        }
        else if (!operation.NeedsRecovery)
        {
            tasks.Reset(task);
        }
        else
        {
            tasks.Fail(row, task, operation.Code, operation.ErrorArgs);
        }
    }

    /// <summary>这一行轮到的、等着做的任务（有这个许可才算）；不该派返回 null。</summary>
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
        var station = location.IsArm ? null : Station(location.Module);
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

    #region 设备与资源占用查询

    /// <summary>按名字找站点（搬运管理的模块表）；不是站点为 null。</summary>
    private ITransferStation? Station(string name)
    {
        var transfers = _transfers;
        return transfers is not null && transfers.TryGetStation(name, out var station) ? station : null;
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

    /// <summary>这一槽账上空着、也没被搬运操作占用。</summary>
    private bool IsSlotFree(string station, int slot)
    {
        return _waferManager is not null && _transfers is not null
            && _waferManager.Get(station, slot) is null && !_transfers.IsSlotLocked(station, slot);
    }

    /// <summary>站点上第一个空着、没被占用的槽；没有返回 0。</summary>
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

    /// <summary>机械手可执行任务：启用、在线、空闲、没有搬运操作。</summary>
    private bool IsRobotFree(IRobot robot)
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

            if (source.IsArm
                ? !string.Equals(robot.Name, source.Module, StringComparison.OrdinalIgnoreCase)
                : !robot.TryGetStation(source.Module, out _))
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

            bool pick = task.Kind == StationTaskAction.Pick;
            tasks.Start(row, task, pick ? source.Module : target, pick ? source.Slot : targetSlot, robot.Name, operation);
            return true;
        }

        return false;
    }
}
