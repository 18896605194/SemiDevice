using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 调度引擎
/// </summary>
[Component(description: "调度引擎：从任务表拿任务执行（站内任务交给站点、取放交给搬运管理），做完记回任务表")]
public class SchedulerComponent : ComponentBase
{
    /// <summary>
    /// 搬运管理
    /// </summary>
    private TransferManager? _transfers;
    /// <summary>
    /// 晶圆账
    /// </summary>
    private WaferManagerComponent? _waferManager;

    /// <summary>
    /// 派这一拍的任务。rows 是有许可的行，按优先级排好（CJ 队列先后、PJ 在 CJ 里的先后、PJ 里的投片顺序）。Manual 下 Job 组件不调。
    /// </summary>
    public virtual void Dispatch(IReadOnlyList<TaskRow> rows, BaseTaskComponent tasks)
    {
        _transfers = TransferManager.Current;
        _waferManager = WaferManagerComponent.Current;
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
    public virtual void Collect(BaseTaskComponent tasks)
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
    private static void CollectTransfer(TaskRow row, WaferTask task, TransferRoutine operation, BaseTaskComponent tasks)
    {
        if (task.Kind == StationTaskAction.Pick && operation.HasPicked)
        {
            var place = row.NextPlace(task);
            if (place is null)
            {
                return;
            }

            tasks.Done(row, task, operation.Arm);
            tasks.Start(row, place, operation.Target.Name, operation.TargetSlot, operation.Robot.Name, operation);
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
    protected static WaferTask? NextTask(TaskRow row, TaskPermission needed)
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
    protected virtual void StartStationTask(TaskRow row, WaferTask task, BaseTaskComponent tasks)
    {
        var location = row.ExpectedLocation;
        var station = location.IsArm ? null : Station(location.Module);
        if (station is null)
        {
            return;
        }

        var operation = station.StartTask(new StationTaskRequest
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
    protected virtual bool StartTransfer(TaskRow row, WaferTask task, BaseTaskComponent tasks)
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

        foreach (string station in OrderTargets(row, place))
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

    /// <summary>
    /// 站点组里按什么先后挑：默认 sc.xml 的先后（流程配方里勾的先后），挑第一个能放的。机型要轮着用、挑最先空出来的就重写。
    /// </summary>
    protected virtual IEnumerable<string> OrderTargets(TaskRow row, WaferTask place)
    {
        return place.Stations;
    }

    #region 设备与资源占用查询

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

    /// <summary>这一槽账上空着、也没被搬运操作占用。</summary>
    protected bool IsSlotFree(string station, int slot)
    {
        return _waferManager is not null && _transfers is not null
            && _waferManager.Get(station, slot) is null && !_transfers.IsSlotLocked(station, slot);
    }

    /// <summary>站点上第一个空着、没被占用的槽；没有返回 0。</summary>
    protected int FreeSlot(string station)
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

    /// <summary>机械手可执行任务：启用、空闲、没有搬运操作。</summary>
    protected bool IsRobotFree(IRobot robot)
    {
        if (_transfers?.IsRobotInTransfer(robot.Name) == true || robot.State != ModuleState.Idle)
        {
            return false;
        }

        return robot is not BaseModule module || module.IsEnabled;
    }

    #endregion

    /// <summary>
    /// 找机械手、启动实际搬运操作并挂到当前任务上；取片确认后才进入放片任务。
    /// 目标槽和机械手在启动时一起占住，避免取片后没有位置可放。
    /// 只放片（片在机械手手上）就用拿着片的那台。
    /// </summary>
    private bool StartTransfer(TaskRow row, WaferTask task, (string Module, int Slot, bool IsArm) source, string target, int targetSlot, BaseTaskComponent tasks)
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
